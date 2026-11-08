using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using DiskLens.Core.Models;

namespace DiskLens.Core.Services;

/// <summary>
/// Reads physical disks, partitions (including hidden EFI / Recovery / MSR ones) and drive letters.
/// Partition details come from the Windows Storage WMI provider (the same data as Disk Management).
/// </summary>
public static class DiskInfoService
{
    private const long MinGapBytes = 16L * 1024 * 1024;

    public static StorageSnapshot Load()
    {
        var snapshot = new StorageSnapshot();
        char windowsLetter = char.ToUpperInvariant(Path.GetPathRoot(Environment.SystemDirectory)?.FirstOrDefault() ?? 'C');

        foreach (var drive in SafeGetDrives())
        {
            try
            {
                if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                    continue;

                char letter = char.ToUpperInvariant(drive.Name[0]);
                snapshot.Drives.Add(new VolumeDrive
                {
                    Letter = letter,
                    Label = drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    TotalSize = drive.TotalSize,
                    FreeSpace = drive.TotalFreeSpace,
                    DriveType = drive.DriveType,
                    IsWindowsDrive = letter == windowsLetter,
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        try
        {
            LoadPartitions(snapshot, windowsLetter);
        }
        catch (Exception ex)
        {
            snapshot.PartitionError = "Partition details unavailable: " + ex.Message;
        }

        return snapshot;
    }

    /// <summary>Re-reads a drive's size and free space (they change as files are added or removed).</summary>
    public static void RefreshSpace(VolumeDrive drive)
    {
        try
        {
            var info = new DriveInfo(drive.Root);
            if (!info.IsReady) return;
            drive.TotalSize = info.TotalSize;
            drive.FreeSpace = info.TotalFreeSpace;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DriveInfo[] SafeGetDrives()
    {
        try { return DriveInfo.GetDrives(); }
        catch { return Array.Empty<DriveInfo>(); }
    }

    private static void LoadPartitions(StorageSnapshot snapshot, char windowsLetter)
    {
        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        scope.Connect();

        var volumesByPath = new Dictionary<string, VolumeRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Query(scope, "SELECT Path, FileSystemLabel, FileSystem, Size, SizeRemaining FROM MSFT_Volume"))
        {
            if (v["Path"] is string path)
                volumesByPath[path] = new VolumeRow(v["FileSystemLabel"] as string, v["FileSystem"] as string, ToLong(v["Size"]), ToLong(v["SizeRemaining"]));
        }

        var disks = new Dictionary<int, PhysicalDiskInfo>();
        foreach (var d in Query(scope, "SELECT Number, FriendlyName, Size, PartitionStyle, BusType, IsSystem, IsBoot FROM MSFT_Disk"))
        {
            var disk = new PhysicalDiskInfo
            {
                Number = ToInt(d["Number"]),
                FriendlyName = (d["FriendlyName"] as string)?.Trim() ?? "Disk",
                Size = ToLong(d["Size"]),
                PartitionStyle = ToInt(d["PartitionStyle"]) switch { 1 => "MBR", 2 => "GPT", 3 => "RAW", _ => "Unknown" },
                BusType = BusTypeName(ToInt(d["BusType"])),
                IsSystem = d["IsSystem"] is true,
                IsBoot = d["IsBoot"] is true,
            };
            disks[disk.Number] = disk;
        }

        var partitionsByDisk = new Dictionary<int, List<PartitionInfo>>();
        foreach (var p in Query(scope, "SELECT DiskNumber, PartitionNumber, DriveLetter, Offset, Size, GptType, MbrType, IsHidden, IsActive, IsBoot, IsSystem, AccessPaths FROM MSFT_Partition"))
        {
            int diskNumber = ToInt(p["DiskNumber"]);
            char? letter = ToLetter(p["DriveLetter"]);

            VolumeRow? volume = null;
            if (p["AccessPaths"] is string[] accessPaths)
            {
                foreach (var ap in accessPaths)
                {
                    if (volumesByPath.TryGetValue(ap, out var row)) { volume = row; break; }
                }
            }

            var (kind, typeName) = Classify(
                p["GptType"] as string,
                p["MbrType"] is null ? (int?)null : ToInt(p["MbrType"]),
                letter, windowsLetter,
                p["IsActive"] is true, p["IsHidden"] is true);

            var label = volume?.Label;
            var fs = volume?.FileSystem;

            var info = new PartitionInfo
            {
                DiskNumber = diskNumber,
                PartitionNumber = ToInt(p["PartitionNumber"]),
                Offset = ToLong(p["Offset"]),
                Size = ToLong(p["Size"]),
                DriveLetter = letter,
                Label = string.IsNullOrWhiteSpace(label) ? null : label,
                FileSystem = string.IsNullOrWhiteSpace(fs) ? null : fs,
                VolumeSize = volume?.Size,
                FreeSpace = volume?.Remaining,
                Kind = kind,
                TypeName = typeName,
                IsBoot = p["IsBoot"] is true,
                IsSystem = p["IsSystem"] is true,
                IsHidden = p["IsHidden"] is true,
            };

            if (!partitionsByDisk.TryGetValue(diskNumber, out var list))
                partitionsByDisk[diskNumber] = list = new List<PartitionInfo>();
            list.Add(info);

            if (letter is char l)
            {
                var drive = snapshot.Drives.FirstOrDefault(x => x.Letter == l);
                if (drive is not null) drive.DiskNumber = diskNumber;
            }
        }

        foreach (var disk in disks.Values.OrderBy(d => d.Number))
        {
            // The MBR "Extended" entry is only a container for the logical partitions inside it, so it isn't drawn.
            var parts = partitionsByDisk.TryGetValue(disk.Number, out var l)
                ? l.Where(p => p.TypeName != "Extended").OrderBy(p => p.Offset).ToList()
                : new List<PartitionInfo>();
            AddWithGaps(disk, parts);
            snapshot.Disks.Add(disk);
        }
    }

    /// <summary>Adds partitions in order and inserts "Unallocated" segments for gaps of 16 MB or more.</summary>
    internal static void AddWithGaps(PhysicalDiskInfo disk, List<PartitionInfo> ordered)
    {
        long cursor = 0;
        foreach (var part in ordered)
        {
            if (part.Offset - cursor >= MinGapBytes)
                disk.Partitions.Add(Unallocated(disk.Number, cursor, part.Offset - cursor));
            disk.Partitions.Add(part);
            cursor = Math.Max(cursor, part.Offset + part.Size);
        }

        if (disk.Size - cursor >= MinGapBytes)
            disk.Partitions.Add(Unallocated(disk.Number, cursor, disk.Size - cursor));
    }

    private static PartitionInfo Unallocated(int disk, long offset, long size) => new()
    {
        DiskNumber = disk,
        Offset = offset,
        Size = size,
        Kind = PartitionKind.Unallocated,
        TypeName = "Unallocated",
    };

    internal static (PartitionKind Kind, string TypeName) Classify(
        string? gptType, int? mbrType, char? letter, char windowsLetter, bool isActive, bool isHidden)
    {
        string gpt = (gptType ?? "").Trim('{', '}', ' ').ToLowerInvariant();
        switch (gpt)
        {
            case "c12a7328-f81f-11d2-ba4b-00a0c93ec93b": return (PartitionKind.Boot, "EFI System");
            case "e3c9e316-0b5c-4db8-817d-f92df00215ae": return (PartitionKind.Reserved, "Microsoft Reserved");
            case "de94bba4-06d1-4d40-a16a-bfd50179d6ac": return (PartitionKind.Recovery, "Recovery");
            case "0fc63daf-8483-4772-8e79-3d69d8477de4": return (PartitionKind.Other, "Linux");
            case "0657fd6d-a4ab-43c4-84e5-0933c84b4f4f": return (PartitionKind.Other, "Linux swap");
            case "5808c8aa-7e8f-42e0-85d2-e1e90434cfb3":
            case "af9b60a0-1431-4f62-bc68-3311714a69ad": return (PartitionKind.Other, "Dynamic disk");
        }

        if (gpt.Length == 0 && mbrType is int mbr)
        {
            switch (mbr)
            {
                case 0x27: return (PartitionKind.Recovery, "Recovery");
                case 0xEF: return (PartitionKind.Boot, "EFI System");
                case 0x82: return (PartitionKind.Other, "Linux swap");
                case 0x83: return (PartitionKind.Other, "Linux");
                case 0x8E: return (PartitionKind.Other, "Linux LVM");
                case 0x05:
                case 0x0F: return (PartitionKind.Other, "Extended");
            }

            if (letter is null && isActive) return (PartitionKind.Boot, "System Reserved");
        }

        if (letter is char l)
            return (char.ToUpperInvariant(l) == windowsLetter ? PartitionKind.WindowsDrive : PartitionKind.DataDrive, "Basic data");

        return (PartitionKind.Other, isHidden ? "Hidden" : "No drive letter");
    }

    private static string BusTypeName(int busType) => busType switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "FireWire", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID",
        9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD card", 13 => "MMC", 14 => "Virtual",
        15 => "Virtual (file)", 16 => "Storage Spaces", 17 => "NVMe", _ => "Disk",
    };

    private static IEnumerable<ManagementBaseObject> Query(ManagementScope scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
        using var results = searcher.Get();
        foreach (var item in results) yield return item;
    }

    private sealed record VolumeRow(string? Label, string? FileSystem, long Size, long Remaining);

    private static int ToInt(object? value) => value is null ? 0 : Convert.ToInt32(value);
    private static long ToLong(object? value) => value is null ? 0 : Convert.ToInt64(value);

    private static char? ToLetter(object? value)
    {
        if (value is null) return null;
        char c = value is char ch ? ch : Convert.ToChar(Convert.ToUInt16(value));
        return char.IsLetter(c) ? char.ToUpperInvariant(c) : null;
    }
}
