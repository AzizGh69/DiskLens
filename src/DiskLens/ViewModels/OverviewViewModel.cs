using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DiskLens.Core;
using DiskLens.Core.Models;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

public sealed class OverviewViewModel : ObservableObject
{
    private string _summary = "Reading your disks…";
    private string? _partitionError;

    public OverviewViewModel(MainViewModel main)
    {
        OpenDriveCommand = new RelayCommand(p => { if (p is DriveViewModel d) main.ShowDrive(d); });
        ScanDriveCommand = new RelayCommand(p => { if (p is DriveViewModel d) main.ShowDrive(d, startScan: true); });
        ScanAllCommand = RelayCommand.From(() => main.ScanAll(), () => Drives.Count > 0 && !main.IsScanningAny);
    }

    public ObservableCollection<DiskViewModel> Disks { get; } = new();
    public ObservableCollection<DriveViewModel> Drives { get; } = new();

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string? PartitionError { get => _partitionError; private set => SetProperty(ref _partitionError, value); }

    public ICommand OpenDriveCommand { get; }
    public ICommand ScanDriveCommand { get; }
    public ICommand ScanAllCommand { get; }

    public void Load(StorageSnapshot snapshot, System.Collections.Generic.IEnumerable<DriveViewModel> drives)
    {
        Disks.Clear();
        foreach (var disk in snapshot.Disks) Disks.Add(new DiskViewModel(disk));

        Drives.Clear();
        foreach (var d in drives) Drives.Add(d);

        long free = snapshot.Drives.Sum(d => d.FreeSpace);
        long total = snapshot.Drives.Sum(d => d.TotalSize);
        string disks = snapshot.Disks.Count switch { 0 => "", 1 => "1 physical disk · ", var n => $"{n} physical disks · " };
        string drivesText = snapshot.Drives.Count == 1 ? "1 drive" : $"{snapshot.Drives.Count} drives";
        Summary = $"{disks}{drivesText} · {Format.Bytes(free)} free of {Format.Bytes(total)}";
        PartitionError = snapshot.PartitionError;
    }
}

public sealed class DiskViewModel
{
    public DiskViewModel(PhysicalDiskInfo disk)
    {
        Title = $"Disk {disk.Number} · {disk.FriendlyName}";
        Subtitle = $"{disk.BusType} · {disk.PartitionStyle} · {Format.Bytes(disk.Size)} · {disk.Partitions.Count(p => p.Kind != PartitionKind.Unallocated)} partitions"
                   + (disk.IsSystem || disk.IsBoot ? " · Windows boots from this disk" : "");
        foreach (var p in disk.Partitions) Segments.Add(new PartitionViewModel(p));
    }

    public string Title { get; }
    public string Subtitle { get; }
    public ObservableCollection<PartitionViewModel> Segments { get; } = new();
}

public sealed class PartitionViewModel
{
    public PartitionViewModel(PartitionInfo p)
    {
        Name = p.DisplayName;
        Weight = p.Size;
        IsUnallocated = p.Kind == PartitionKind.Unallocated;
        IsWindows = p.Kind == PartitionKind.WindowsDrive;
        HasUsage = p.UsedSpace is not null && p.VolumeSize > 0;
        Used = p.UsedSpace ?? 0;
        Total = p.VolumeSize ?? p.Size;

        string fs = p.FileSystem ?? "";
        Caption = p.Kind switch
        {
            PartitionKind.Unallocated => $"{Format.Bytes(p.Size)} · not in use",
            _ when p.DriveLetter is not null => $"{Format.Bytes(p.Size)} · {fs}",
            _ => string.IsNullOrEmpty(fs) ? Format.Bytes(p.Size) : $"{Format.Bytes(p.Size)} · {fs}",
        };

        string usage = HasUsage ? $"\nUsed {Format.Bytes((long)Used)} · Free {Format.Bytes((long)(Total - Used))}" : "";
        string role = p.Kind switch
        {
            PartitionKind.WindowsDrive => "\nWindows is installed here.",
            PartitionKind.Boot => "\nStarts your PC. Windows needs it — leave it alone.",
            PartitionKind.Recovery => "\nHolds recovery / reset tools (often from the PC maker).",
            PartitionKind.Reserved => "\nSmall area Windows reserves for disk housekeeping.",
            PartitionKind.Unallocated => "\nSpace on the disk that no partition uses.",
            _ when p.TypeName.StartsWith("Linux") => "\nUsed by a Linux installation; Windows can't read it.",
            _ => "",
        };
        ToolTip = $"{Name}{(p.DriveLetter is null ? " (hidden — no drive letter)" : "")}\n{p.TypeName}{(p.PartitionNumber > 0 ? $" · partition {p.PartitionNumber}" : "")} · {Format.Bytes(p.Size)}{(fs.Length > 0 ? " · " + fs : "")}{usage}{role}";
    }

    public string Name { get; }
    public string Caption { get; }
    public string ToolTip { get; }
    public double Weight { get; }
    public bool HasUsage { get; }
    public double Used { get; }
    public double Total { get; }
    public bool IsUnallocated { get; }
    public bool IsWindows { get; }
}
