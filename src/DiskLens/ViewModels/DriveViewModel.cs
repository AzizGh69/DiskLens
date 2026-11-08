using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private int _selectedTab;
    private TypeFilterViewModel? _selectedTypeFilter;
    private List<FileRowViewModel> _allFiles = new();
    private List<AppSize> _appSizes = new();
    private string _appsSummary = "";
    private string _systemSummary = "";
    private long? _restorePointBytes;
    private RecycleGuard? _guard;

    public DriveViewModel(VolumeDrive drive, MainViewModel main)
    {
        Drive = drive;
        _main = main;
        _progressTimer.Tick += (_, _) => UpdateProgress();

        OpenInExplorerCommand = RelayCommand.From(() => Shell.OpenFolder(Drive.Root));
        ScanCommand = RelayCommand.From(StartScan, () => !IsScanning);
        CancelCommand = RelayCommand.From(() => _cts?.Cancel(), () => IsScanning);
        ShowInExplorerCommand = new RelayCommand(p => { if (p is IPathItem item) Shell.ShowInExplorer(item.FullPath); }, HasPath);
        CopyPathCommand = new RelayCommand(p =>
        {
            if (p is IPathItem item && Shell.CopyText(item.FullPath)) _main.ShowBanner($"Copied: {item.FullPath}");
        }, HasPath);
        OpenItemCommand = new RelayCommand(p => OpenItem(p as IPathItem), p => p is IPathItem);
        UninstallWithRevoCommand = new RelayCommand(p => { if (p is AppRowViewModel row) _main.UninstallWithRevo(row.App); }, p => p is AppRowViewModel);
        RecycleCommand = new RelayCommand(p => Recycle(p as PathItemViewModel), p => p is PathItemViewModel { CanRecycle: true } && !IsScanning);
        RunSystemActionCommand = new RelayCommand(p => { if (p is SystemItemViewModel { Item.Action: { } a }) RunSystemAction(a); },
            p => p is SystemItemViewModel { HasAction: true });
        Browser = new FolderBrowserViewModel(GetBlockReason);

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
            char letter = Drive.Letter;
            var (result, restore) = await Task.Run(() =>
            {
                var scan = scanner.Scan(Drive.Root, token);
                return (scan, SystemItemsService.RestorePointBytes(letter));
            }, token);
            _restorePointBytes = restore;
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

    /// <summary>0 = Largest files, 1 = Folders, 2 = Apps, 3 = System.</summary>
    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }

    public ObservableCollection<FileRowViewModel> Files { get; } = new();
    public ObservableCollection<TypeFilterViewModel> TypeFilters { get; } = new();

    public TypeFilterViewModel? SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value)) ApplyTypeFilter();
        }
    }

    public FolderBrowserViewModel Browser { get; }

    public ObservableCollection<AppRowViewModel> Apps { get; } = new();
    public string AppsSummary { get => _appsSummary; private set => SetProperty(ref _appsSummary, value); }

    public ICommand ShowInExplorerCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand UninstallWithRevoCommand { get; }
    public ICommand RecycleCommand { get; }

    public ObservableCollection<SystemItemViewModel> SystemItems { get; } = new();
    public string SystemSummary { get => _systemSummary; private set => SetProperty(ref _systemSummary, value); }
    public ICommand RunSystemActionCommand { get; }

    /// <summary>Opens the Windows tool for a system item. DiskLens itself never deletes system files.</summary>
    private static void RunSystemAction(SystemAction action) => Shell.Start(action.Target, action.Arguments);

    /// <summary>"Uninstall with Revo", or a generic label when Revo isn't installed.</summary>
    public string UninstallActionText => _main.UninstallActionText;

    private static bool HasPath(object? p) => p is IPathItem { FullPath.Length: > 0 };

    /// <summary>Double-click / Enter: folders (and app folders) open in the Folders tab, files are shown in Explorer.</summary>
    public void OpenItem(IPathItem? item)
    {
        if (item is null) return;
        var node = item switch
        {
            FolderItemViewModel f => f.Node,
            AppRowViewModel a => a.Folder,
            _ => null,
        };
        if (node is not null)
        {
            Browser.NavigateTo(node);
            SelectedTab = 1;
            return;
        }

        if (item.FullPath.Length > 0) Shell.ShowInExplorer(item.FullPath);
    }

    /// <summary>Why an item can't go to the Recycle Bin, or null if it can.</summary>
    private string? GetBlockReason(IPathItem item) => _guard?.BlockReason(item.FullPath) ?? (_guard is null ? "Scan the drive first." : null);

    private RecycleGuard BuildGuard()
    {
        var appFolders = _main.InstalledApps
            .Where(a => a.InstallLocation is not null)
            .Select(a => (a.InstallLocation!, a.Name))
            .Concat(_appSizes.Where(a => a.App.IsStoreApp && a.App.InstallLocation is not null).Select(a => (a.App.InstallLocation!, a.App.Name)));
        return new RecycleGuard(Drive.Root, Drive.HasRecycleBin, appFolders);
    }

    /// <summary>Asks for confirmation, then moves the item to the Recycle Bin and updates every view.</summary>
    private void Recycle(PathItemViewModel? item)
    {
        if (item is null || _result is null || _guard is null) return;
        var owner = Application.Current?.MainWindow;

        // Check again right before acting, in case anything changed since the list was built.
        if (_guard.BlockReason(item.FullPath) is string reason)
        {
            MessageBox.Show(owner!, reason, "Can't move to the Recycle Bin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string kind = item.IsFolder ? "folder" : "file";
        string question =
            $"Move this {kind} to the Recycle Bin?\n\n" +
            $"{item.Name}\n{Format.Bytes(item.Size)} · {item.FullPath}\n\n" +
            "You can restore it from the Recycle Bin if you change your mind. " +
            "The space is freed once you empty the Recycle Bin.";
        if (MessageBox.Show(owner!, question, "Move to Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        IntPtr hwnd = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        RecycleOutcome outcome;
        try
        {
            outcome = RecycleBin.Move(item.FullPath, hwnd);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner!, $"Windows couldn't move \"{item.Name}\" to the Recycle Bin.\n\n{ex.Message}", "DiskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        switch (outcome)
        {
            case RecycleOutcome.Moved:
                _result.ApplyRecycled(item.FullPath, item.IsFolder, item.Size);
                RefreshResults();
                _main.ShowBanner($"Moved \"{item.Name}\" ({Format.Bytes(item.Size)}) to the Recycle Bin. Empty the Recycle Bin to get the space back.");
                break;
            case RecycleOutcome.Partial:
                _main.ShowBanner($"Some files in \"{item.Name}\" were in use and stayed where they were. The rest is in the Recycle Bin — rescan to see the new sizes.");
                break;
            default:
                _main.ShowBanner($"\"{item.Name}\" wasn't moved.");
                break;
        }
    }

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
        _appSizes = InstalledAppsService.Measure(_result, _main.InstalledApps, Drive.IsWindowsDrive);
        _guard = BuildGuard();

        BuildCategories(_result);
        BuildFileAnswer(_result);
        BuildFolderAnswer(_result);
        BuildAppAnswer();
        BuildFileList(_result);
        BuildAppList();
        BuildSystemItems(_result);
        Browser.Reset(Browser.Current is { } current && IsInTree(current, _result.Root) ? current : _result.Root);
    }

    private void BuildSystemItems(ScanResult result)
    {
        var items = SystemItemsService.Find(result, _restorePointBytes);
        long largest = items.Count > 0 ? items[0].Size : 0;
        SystemItems.Clear();
        foreach (var item in items) SystemItems.Add(new SystemItemViewModel(item, largest));

        string restoreNote = _restorePointBytes is null && Drive.FileSystem == "NTFS"
            ? " · Restore points can only be measured when DiskLens runs as administrator."
            : "";
        SystemSummary = items.Count == 0
            ? "No large system files on this drive." + restoreNote
            : $"{items.Count} items · {Format.Bytes(items.Sum(i => i.Size))}. DiskLens never deletes these — each card says what is safe and opens the Windows tool for it.{restoreNote}";

        if (items.Count == 0)
        {
            BiggestSystemItem.Clear("No large system files on this drive.");
            return;
        }

        var top = SystemItems[0];
        var action = top.Item.Action is { } a
            ? (top.ActionText, RelayCommand.From(() => RunSystemAction(a)))
            : ("What can I do?", RelayCommand.From(() => SelectedTab = 3));
        BiggestSystemItem.Set(top.SizeText, top.Name, top.WhatItIs,
            SystemItems.Skip(1).Take(2).Select((s, i) => $"{i + 2}. {s.Name} · {s.SizeText}"),
            action.Item1, action.Item2);
    }

    private void BuildCategories(ScanResult result)
    {
        var appFolders = _appSizes.Where(a => a.Folder is not null).Select(a => a.Folder!);
        var b = Categorizer.Compute(result, Drive.UsedSpace, Drive.FreeSpace, appFolders);
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
            "Look inside", RelayCommand.From(() =>
            {
                Browser.NavigateTo(top);
                SelectedTab = 1;
            }));
    }

    private void BuildAppAnswer()
    {
        if (_appSizes.Count == 0)
        {
            HeaviestApp.Clear(Drive.IsWindowsDrive || _main.InstalledApps.Count == 0
                ? "No installed apps found."
                : "No installed apps live on this drive.");
            return;
        }

        var top = _appSizes[0];
        string detail = top.App.InstallLocation ?? (top.App.Publisher.Length > 0 ? top.App.Publisher : "Folder not recorded");
        string size = (top.IsEstimate ? "≈ " : "") + Format.Bytes(top.Size);
        var app = top.App;
        HeaviestApp.Set(size, app.Name, detail,
            _appSizes.Skip(1).Take(2).Select((a, i) => $"{i + 2}. {a.App.Name} · {(a.IsEstimate ? "≈ " : "")}{Format.Bytes(a.Size)}"),
            UninstallActionText, RelayCommand.From(() => _main.UninstallWithRevo(app)));
    }

    private void BuildAppList()
    {
        long largest = _appSizes.Count > 0 ? _appSizes[0].Size : 0;
        Apps.Clear();
        foreach (var size in _appSizes) Apps.Add(new AppRowViewModel(size, largest));

        long total = _appSizes.Sum(a => a.Size);
        int estimates = _appSizes.Count(a => a.IsEstimate);
        string estimateNote = estimates > 0 ? " · ≈ means the size the app's installer reported, because its folder couldn't be measured" : "";
        AppsSummary = _appSizes.Count == 0
            ? "No installed apps were found on this drive."
            : $"{Format.Count(_appSizes.Count)} apps · {Format.Bytes(total)} in total{estimateNote}";
    }

    private void BuildFileList(ScanResult result)
    {
        long largest = result.LargestFiles.Count > 0 ? result.LargestFiles[0].Size : 0;
        _allFiles = result.LargestFiles.Select(f => new FileRowViewModel(f, largest)).ToList();
        foreach (var row in _allFiles) row.BlockReason = GetBlockReason(row);

        string? previous = SelectedTypeFilter?.Name;
        TypeFilters.Clear();
        TypeFilters.Add(new TypeFilterViewModel(TypeFilterViewModel.AllName, _allFiles.Count, _allFiles.Sum(f => f.Size)));
        foreach (var group in FileTypes.Groups)
        {
            var inGroup = _allFiles.Where(f => f.Group == group).ToList();
            if (inGroup.Count > 0) TypeFilters.Add(new TypeFilterViewModel(group, inGroup.Count, inGroup.Sum(f => f.Size)));
        }

        _selectedTypeFilter = TypeFilters.FirstOrDefault(f => f.Name == previous) ?? TypeFilters[0];
        OnPropertyChanged(nameof(SelectedTypeFilter));
        ApplyTypeFilter();
    }

    private void ApplyTypeFilter()
    {
        string name = SelectedTypeFilter?.Name ?? TypeFilterViewModel.AllName;
        Files.Clear();
        foreach (var row in _allFiles)
            if (name == TypeFilterViewModel.AllName || row.Group == name) Files.Add(row);
    }

    private static bool IsInTree(FolderNode node, FolderNode root)
    {
        var top = node;
        while (top.Parent is not null) top = top.Parent;
        return ReferenceEquals(top, root);
    }

    internal void ApplySnapshotOptions(SnapshotOptions options)
    {
        if (options.Folder is string folder && _result?.FindFolder(folder) is { } node) Browser.NavigateTo(node);
        if (options.Tab is int tab) SelectedTab = tab;
    }
}
