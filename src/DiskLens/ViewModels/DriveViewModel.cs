using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using DiskLens.Core;
using DiskLens.Core.Models;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

public sealed class DriveViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _isScanning;

    public DriveViewModel(VolumeDrive drive, MainViewModel main)
    {
        Drive = drive;
        _main = main;
        OpenInExplorerCommand = RelayCommand.From(() => Shell.OpenFolder(Drive.Root));
    }

    public VolumeDrive Drive { get; }

    public string Title => Drive.DisplayName;
    public string Letter => $"{Drive.Letter}:";
    public string Label => string.IsNullOrWhiteSpace(Drive.Label) ? "Local disk" : Drive.Label;

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

    public bool IsScanning { get => _isScanning; private set => SetProperty(ref _isScanning, value); }

    public ICommand OpenInExplorerCommand { get; }

    public string ScanButtonText => "Scan";

    public void StartScan() => _ = ScanAsync();

    public Task ScanAsync() => Task.CompletedTask;

    internal void ApplySnapshotOptions(SnapshotOptions options) { }
}
