using System.Collections.Generic;
using System.IO;

namespace DiskLens.Core.Models;

public enum PartitionKind
{
    /// <summary>The drive Windows runs from (usually C:).</summary>
    WindowsDrive,
    /// <summary>Any other partition with a drive letter.</summary>
    DataDrive,
    /// <summary>EFI System / System Reserved boot partition.</summary>
    Boot,
    Recovery,
    /// <summary>Microsoft Reserved (MSR) and other hidden housekeeping partitions.</summary>
    Reserved,
    Other,
    Unallocated,
}

public sealed class PartitionInfo
{
    public int DiskNumber { get; init; }
    public int PartitionNumber { get; init; }
    public long Offset { get; init; }
    public long Size { get; init; }
    public char? DriveLetter { get; init; }
    public string? Label { get; init; }
    public string? FileSystem { get; init; }
    public long? VolumeSize { get; init; }
    public long? FreeSpace { get; init; }
    public PartitionKind Kind { get; init; }
    public string TypeName { get; init; } = "Partition";
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public bool IsHidden { get; init; }

    public long? UsedSpace => VolumeSize is long total && FreeSpace is long free ? total - free : null;

    public string DisplayName => DriveLetter is char c
        ? (string.IsNullOrWhiteSpace(Label) ? $"{c}:" : $"{c}: {Label}")
        : TypeName;
}

public sealed class PhysicalDiskInfo
{
    public int Number { get; init; }
    public string FriendlyName { get; init; } = "Disk";
    public long Size { get; init; }
    public string PartitionStyle { get; init; } = "Unknown";
    public string BusType { get; init; } = "Unknown";
    public bool IsSystem { get; init; }
    public bool IsBoot { get; init; }

    /// <summary>Partitions in on-disk order, including unallocated gaps.</summary>
    public List<PartitionInfo> Partitions { get; } = new();
}

/// <summary>A mounted drive with a letter (C:, D:, ...).</summary>
public sealed class VolumeDrive
{
    public char Letter { get; init; }
    public string Root => $"{Letter}:\\";
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public DriveType DriveType { get; init; }
    public bool IsWindowsDrive { get; init; }
    public int? DiskNumber { get; set; }

    public long UsedSpace => TotalSize - FreeSpace;

    /// <summary>NTFS/ReFS fixed drives have a Recycle Bin; FAT drives and removable media delete permanently.</summary>
    public bool HasRecycleBin =>
        DriveType == DriveType.Fixed &&
        (FileSystem.Equals("NTFS", System.StringComparison.OrdinalIgnoreCase) ||
         FileSystem.Equals("ReFS", System.StringComparison.OrdinalIgnoreCase));

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? $"{Letter}:" : $"{Letter}: {Label}";
}

public sealed class StorageSnapshot
{
    public List<PhysicalDiskInfo> Disks { get; } = new();
    public List<VolumeDrive> Drives { get; } = new();

    /// <summary>Set when partition details couldn't be read (drives still work).</summary>
    public string? PartitionError { get; set; }
}
