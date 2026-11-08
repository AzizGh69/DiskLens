using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using DiskLens.Core;
using DiskLens.Core.Models;
using DiskLens.Core.Services;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

public sealed class DriveViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CancellationTokenSource? _cts;
    private Scanner? _scanner;
    private Task? _scanTask;
    private ScanResult? _result;

    private bool _isScanning;
    private bool _hasResult;
    private double _progressPercent;
    private string _progressText = "";
    private string _progressPath = "";
    private string _resultSummary = "";
    private string? _errorText;

    public DriveViewModel(VolumeDrive drive, MainViewModel main)
    {
        Drive = drive;
        _main = main;
        _progressTimer.Tick += (_, _) => UpdateProgress();

        OpenInExplorerCommand = RelayCommand.From(() => Shell.OpenFolder(Drive.Root));
        ScanCommand = RelayCommand.From(StartScan, () => !IsScanning);
        CancelCommand = RelayCommand.From(() => _cts?.Cancel(), () => IsScanning);

        HeaviestFile = new AnswerCardViewModel("Heaviest file", "");
        HeaviestFolder = new AnswerCardViewModel("Heaviest folder", "");
        HeaviestApp = new AnswerCardViewModel("Heaviest app", "");
        BiggestSystemItem = new AnswerCardViewModel("Biggest system item", "");
    }

    public VolumeDrive Drive { get; }
    public ScanResult? Result => _result;

    // ---- Drive header -------------------------------------------------------------------------

    public string Title => Drive.DisplayName;
    public string Letter => $"{Drive.Letter}:";

    public string Details
    {
        get
        {
            string type = Drive.DriveType == DriveType.Removable ? "Removable · " : "";
            string disk = Drive.DiskNumber is int n ? $" · Disk {n}" : "";
            string windows = Drive.IsWindowsDrive ? " · Windows drive" : "";
            return $"{type}{Drive.FileSystem} · {Format.Bytes(Drive.TotalSize)}{disk}{windows}";
        }
    }

    public double Used => Drive.UsedSpace;
    public double Total => Drive.TotalSize;
    public double FreePercent => Format.Share(Drive.FreeSpace, Drive.TotalSize);
    public string FreeText => $"{Format.Bytes(Drive.FreeSpace)} free of {Format.Bytes(Drive.TotalSize)}";
    public string UsageText => $"Used {Format.Bytes(Drive.UsedSpace)} · Free {Format.Bytes(Drive.FreeSpace)} ({Format.Percent(FreePercent)})";

    /// <summary>Less than 10% free: Windows starts to slow down and updates can fail.</summary>
    public bool IsLow => FreePercent < 10;

    /// <summary>Second line in the sidebar: free space, or scan progress while scanning.</summary>
    public string SidebarCaption => IsScanning ? $"Scanning… {ProgressPercent:0}%" : FreeText;

    public ICommand OpenInExplorerCommand { get; }

    // ---- Scanning -----------------------------------------------------------------------------

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(SidebarCaption));
                OnPropertyChanged(nameof(ScanButtonText));
            }
        }
    }

    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (SetProperty(ref _hasResult, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ScanButtonText));
            }
        }
    }

    public bool ShowEmptyState => !IsScanning && !HasResult;
    public string ScanButtonText => IsScanning ? "Scanning…" : HasResult ? "Rescan" : "Scan";

    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }
    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
    public string ProgressPath { get => _progressPath; private set => SetProperty(ref _progressPath, value); }
    public string ResultSummary { get => _resultSummary; private set => SetProperty(ref _resultSummary, value); }
    public string? ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }

    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }

    public void StartScan() => _ = ScanAsync();

    public Task ScanAsync()
    {
        if (IsScanning && _scanTask is not null) return _scanTask;
        _scanTask = RunScanAsync();
        return _scanTask;
    }

    private async Task RunScanAsync()
    {
        DiskInfoService.RefreshSpace(Drive);
        RaiseSpaceChanged();

        _cts = new CancellationTokenSource();
        _scanner = new Scanner();
        ErrorText = null;
        ProgressPercent = 0;
        ProgressText = "Starting…";
        ProgressPath = Drive.Root;
        IsScanning = true;
        _main.NotifyScanStateChanged();
        _progressTimer.Start();

        try
        {
            var scanner = _scanner;
            var token = _cts.Token;
            var result = await Task.Run(() => scanner.Scan(Drive.Root, token), token);
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            // Keep whatever result was there before.
        }
        catch (Exception ex)
        {
            ErrorText = "The scan stopped: " + ex.Message;
        }
        finally
        {
            _progressTimer.Stop();
            _cts.Dispose();
            _cts = null;
            _scanner = null;
            IsScanning = false;
            _main.NotifyScanStateChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void UpdateProgress()
    {
        if (_scanner is null) return;
        var p = _scanner.Progress;
        ProgressPercent = Math.Min(99, p.Bytes * 100.0 / Math.Max(1, Drive.UsedSpace));
        ProgressText = $"{Format.Count(p.Files)} files · {Format.Bytes(p.Bytes)} of about {Format.Bytes(Drive.UsedSpace)}";
        ProgressPath = p.CurrentFolder;
        OnPropertyChanged(nameof(SidebarCaption));
    }

    private void RaiseSpaceChanged()
    {
        OnPropertyChanged(nameof(Used));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(FreePercent));
        OnPropertyChanged(nameof(FreeText));
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(SidebarCaption));
    }

    // ---- Results ------------------------------------------------------------------------------

    public AnswerCardViewModel HeaviestFile { get; }
    public AnswerCardViewModel HeaviestFolder { get; }
    public AnswerCardViewModel HeaviestApp { get; }
    public AnswerCardViewModel BiggestSystemItem { get; }

    public ObservableCollection<CategorySliceViewModel> Categories { get; } = new();

    private void ApplyResult(ScanResult result)
    {
        _result = result;
        OnPropertyChanged(nameof(Result));
        RefreshResults();

        string unreadable = result.UnreadableCount > 0
            ? $" · {Format.Count(result.UnreadableCount)} folders are locked by Windows and couldn't be read"
            : "";
        ResultSummary = $"Scanned {Format.Count(result.FileCount)} files in {Format.Count(result.FolderCount)} folders in {Format.Duration(result.Duration)} " +
                        $"(at {result.FinishedAt:t}){unreadable}";
        HasResult = true;
    }

    /// <summary>Rebuilds every answer and list from the current scan result.</summary>
    private void RefreshResults()
    {
        if (_result is null) return;
        BuildCategories(_result);
        BuildFileAnswer(_result);
        BuildFolderAnswer(_result);
        HeaviestApp.Clear("App sizes are coming in the next step.");
        BiggestSystemItem.Clear("System items are coming in a later step.");
    }

    private void BuildCategories(ScanResult result)
    {
        var b = Categorizer.Compute(result, Drive.UsedSpace, Drive.FreeSpace);
        long total = b.Total;

        Categories.Clear();
        Categories.Add(new CategorySliceViewModel("System", "Windows, the page file, hibernation and other files Windows needs.", b.System, total, "Cat.System"));
        Categories.Add(new CategorySliceViewModel("Apps & app data", "Program Files, ProgramData, AppData and folders of installed apps and games.", b.Apps, total, "Cat.Apps"));
        Categories.Add(new CategorySliceViewModel("Your files", "Documents, Downloads, Desktop, Pictures, Videos and everything else in your user folder.", b.User, total, "Cat.User"));
        Categories.Add(new CategorySliceViewModel("Other", "Folders outside Windows, apps and user folders.", b.Other, total, "Cat.Other"));
        Categories.Add(new CategorySliceViewModel("Hidden from the scan", "Space Windows uses but won't let any app list: restore points, protected folders, file-system data.", b.Unreadable, total, "Cat.Unreadable"));
        Categories.Add(new CategorySliceViewModel("Free", "Space you can still use.", b.Free, total, "Cat.Free"));
    }

    private void BuildFileAnswer(ScanResult result)
    {
        var files = result.LargestFiles;
        if (files.Count == 0)
        {
            HeaviestFile.Clear("No files found.");
            return;
        }

        var top = files[0];
        HeaviestFile.Set(Format.Bytes(top.Size), top.Name, top.Folder,
            files.Skip(1).Take(2).Select((f, i) => $"{i + 2}. {f.Name} · {Format.Bytes(f.Size)}"),
            "Show in Explorer", RelayCommand.From(() => Shell.ShowInExplorer(top.FullPath)));
    }

    private void BuildFolderAnswer(ScanResult result)
    {
        var folders = Categorizer.HeaviestFolders(result, 3);
        if (folders.Count == 0)
        {
            HeaviestFolder.Clear("No folders found.");
            return;
        }

        var top = folders[0];
        HeaviestFolder.Set(Format.Bytes(top.Size), top.Name, top.FullPath,
            folders.Skip(1).Select((f, i) => $"{i + 2}. {f.Name} · {Format.Bytes(f.Size)}"),
            "Open folder", RelayCommand.From(() => Shell.OpenFolder(top.FullPath)));
    }

    internal void ApplySnapshotOptions(SnapshotOptions options) { }
}
