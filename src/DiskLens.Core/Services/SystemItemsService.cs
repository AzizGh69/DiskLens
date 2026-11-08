using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using DiskLens.Core.Models;

namespace DiskLens.Core.Services;

/// <summary>
/// Opens a Windows tool where the person decides what to clean (DiskLens never deletes system files itself).
/// <paramref name="Target"/> is a program or a URI such as <c>ms-settings:storagesense</c>.
/// </summary>
public sealed record SystemAction(string Label, string Target, string Arguments = "");

public sealed class SystemItem
{
    public string Name { get; init; } = "";

    /// <summary>File or folder on disk, or null for items that aren't a single path (e.g. restore points).</summary>
    public string? Path { get; init; }

    public long Size { get; init; }
    public bool IsFolder { get; init; }

    /// <summary>Plain-English explanation.</summary>
    public string WhatItIs { get; init; } = "";

    /// <summary>What can be done about it, safely. Shown only — never run by DiskLens.</summary>
    public string Tip { get; init; } = "";

    /// <summary>Caveat about the size, e.g. hard links make WinSxS look bigger than it is.</summary>
    public string? SizeNote { get; init; }

    public SystemAction? Action { get; init; }
}

/// <summary>Finds the heavy things Windows keeps on a drive and explains them.</summary>
public static class SystemItemsService
{
    private const long MinSize = 1024 * 1024; // hide items under 1 MB

    /// <param name="restorePointBytes">Space used by restore points on this drive (from <see cref="RestorePointBytes"/>), if known.</param>
    public static List<SystemItem> Find(ScanResult scan, long? restorePointBytes = null)
    {
        var items = new List<SystemItem>();
        string letter = scan.RootPath.Substring(0, 1).ToUpperInvariant();
        var diskCleanup = new SystemAction("Open Disk Cleanup", "cleanmgr.exe", $"/d {letter}");
        var storageSettings = new SystemAction("Open Storage settings", "ms-settings:storagesense");

        // ---- Files in the drive root -------------------------------------------------------------
        foreach (var file in scan.RootFiles)
        {
            switch (file.Name.ToLowerInvariant())
            {
                case "pagefile.sys":
                    items.Add(new SystemItem
                    {
                        Name = "Page file (virtual memory)", Path = file.FullPath, Size = file.Size,
                        WhatItIs = "Windows moves memory here when your RAM is full, so apps don't crash.",
                        Tip = "Best left on \"System managed\". You can change it under Advanced system settings → Performance → Virtual memory, but a page file that is too small can make apps crash.",
                        Action = new SystemAction("Open Performance options", "SystemPropertiesPerformance.exe"),
                    });
                    break;
                case "hiberfil.sys":
                    items.Add(new SystemItem
                    {
                        Name = "Hibernation file", Path = file.FullPath, Size = file.Size,
                        WhatItIs = "Holds the contents of memory when the PC hibernates. Fast Startup uses it too.",
                        Tip = "If you never use Hibernate, running \"powercfg /h off\" in an administrator terminal removes this file (and turns off Hibernate and Fast Startup). DiskLens doesn't run it for you.",
                    });
                    break;
                case "swapfile.sys":
                    items.Add(new SystemItem
                    {
                        Name = "Swap file", Path = file.FullPath, Size = file.Size,
                        WhatItIs = "Used by Windows to pause and resume Store apps.",
                        Tip = "Managed by Windows together with the page file. Leave it as it is.",
                    });
                    break;
            }
        }

        // ---- Folders from the scan -------------------------------------------------------------
        AddFolder(items, scan, @"Windows.old", "Previous Windows installation",
            "Your old copy of Windows, kept after an upgrade so you can go back. Windows removes it by itself after about 10 days.",
            "If you're happy with the upgrade, remove it with Disk Cleanup → Clean up system files → \"Previous Windows installation(s)\".",
            diskCleanup);

        AddFolder(items, scan, @"Windows\WinSxS", "Windows component store (WinSxS)",
            "Copies of Windows system files used to install updates, add features and repair Windows.",
            "Never delete files here by hand. Disk Cleanup → Clean up system files → \"Windows Update Cleanup\" shrinks it safely.",
            diskCleanup,
            sizeNote: "Looks bigger than it is: many of these files are the same files as in System32 (hard links), so they only take space once.");

        AddFolder(items, scan, @"Windows\SoftwareDistribution\Download", "Windows Update downloads",
            "Update packages Windows downloaded. Once installed they are no longer needed.",
            "Disk Cleanup → Clean up system files → \"Windows Update Cleanup\" / Storage settings → Temporary files removes them.",
            diskCleanup);

        AddFolder(items, scan, @"Windows\Installer", "Installer cache (Windows\\Installer)",
            "Copies of the installers Windows needs to repair, update or uninstall your apps.",
            "Don't delete files here by hand — apps can stop uninstalling or updating. Removing apps you don't use (with Revo) makes it smaller.",
            null);

        AddFolder(items, scan, @"Windows\System32\DriverStore\FileRepository", "Driver store",
            "A copy of every driver that has been installed, so Windows can reinstall them.",
            "Leave it. Old driver versions can be removed with Disk Cleanup → Clean up system files → \"Device driver packages\".",
            diskCleanup);

        AddFolder(items, scan, @"Windows\Temp", "Windows temporary files",
            "Temporary files left behind by Windows, installers and updates.",
            "Storage settings → Temporary files removes the ones that are safe to delete.",
            storageSettings);

        AddFolder(items, scan, @"ProgramData\Microsoft\Windows\WER", "Error reports",
            "Crash reports and dumps collected when apps or Windows crash.",
            "Disk Cleanup → \"Windows error reports and feedback diagnostics\" removes them.",
            diskCleanup);

        AddFolder(items, scan, @"Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", "Delivery Optimization cache",
            "Update files Windows keeps to share with other PCs on your network.",
            "Disk Cleanup → \"Delivery Optimization Files\" removes them.",
            diskCleanup);

        AddFolder(items, scan, @"Windows\Minidump", "Crash dumps (small)",
            "Small memory dumps written when Windows crashed (blue screen).",
            "Disk Cleanup → \"System error memory dump files\" removes them.",
            diskCleanup);

        AddFile(items, System.IO.Path.Join(scan.RootPath, @"Windows\MEMORY.DMP"), "Crash dump (full)",
            "A full memory dump written the last time Windows crashed (blue screen).",
            "Disk Cleanup → Clean up system files → \"System error memory dump files\" removes it.",
            diskCleanup);

        // Temp folders of every user profile, as one item
        var userTemps = scan.Root.FindChild("Users")?.Children
            .Select(u => u.FindChild("AppData")?.FindChild("Local")?.FindChild("Temp"))
            .Where(t => t is not null && t.Size > 0)
            .Select(t => t!)
            .ToList() ?? new List<FolderNode>();
        if (userTemps.Count > 0)
        {
            var biggest = userTemps.OrderByDescending(t => t.Size).First();
            items.Add(new SystemItem
            {
                Name = "Apps' temporary files",
                Path = biggest.FullPath,
                IsFolder = true,
                Size = userTemps.Sum(t => t.Size),
                WhatItIs = userTemps.Count == 1
                    ? "Temporary files apps create while they run and often forget to delete."
                    : $"Temporary files apps create while they run, in {userTemps.Count} user profiles.",
                Tip = "Close your apps, then Storage settings → Temporary files removes what's safe to delete.",
                Action = storageSettings,
            });
        }

        // Recycle Bin
        if (scan.Root.FindChild("$Recycle.Bin") is { Size: > 0 } bin)
        {
            items.Add(new SystemItem
            {
                Name = "Recycle Bin", Path = bin.FullPath, IsFolder = true, Size = bin.Size,
                WhatItIs = "Files you deleted. They keep taking space until the Recycle Bin is emptied.",
                Tip = "Look through it and empty it (right-click the Recycle Bin on the desktop → Empty Recycle Bin).",
                Action = new SystemAction("Open Recycle Bin", "explorer.exe", "shell:RecycleBinFolder"),
            });
        }

        // System Restore points (stored in System Volume Information, which nobody can list)
        if (restorePointBytes is long restore && restore > 0)
        {
            items.Add(new SystemItem
            {
                Name = "System Restore points",
                Path = null,
                Size = restore,
                WhatItIs = "Snapshots Windows takes before updates and installs, so you can undo changes. They live in a protected folder no app can open.",
                Tip = "System Properties → System Protection → Configure lets you lower the space restore points may use (the oldest ones are removed first).",
                Action = new SystemAction("Open System Protection", "SystemPropertiesProtection.exe"),
            });
        }

        return items.Where(i => i.Size >= MinSize).OrderByDescending(i => i.Size).ToList();
    }

    private static void AddFolder(List<SystemItem> items, ScanResult scan, string relativePath, string name, string what, string tip,
        SystemAction? action, string? sizeNote = null)
    {
        var node = scan.FindFolder(System.IO.Path.Join(scan.RootPath, relativePath));
        if (node is null || node.Size <= 0) return;
        items.Add(new SystemItem
        {
            Name = name, Path = node.FullPath, IsFolder = true, Size = node.Size,
            WhatItIs = what, Tip = tip, Action = action, SizeNote = sizeNote,
        });
    }

    private static void AddFile(List<SystemItem> items, string path, string name, string what, string tip, SystemAction? action)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            items.Add(new SystemItem { Name = name, Path = path, Size = info.Length, WhatItIs = what, Tip = tip, Action = action });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Space used by System Restore / shadow copies on a volume (needs administrator rights), or null.</summary>
    public static long? RestorePointBytes(char driveLetter)
    {
        try
        {
            string? volumeGuid = null;
            using (var volumes = new ManagementObjectSearcher(@"root\cimv2", $"SELECT DeviceID FROM Win32_Volume WHERE DriveLetter = '{char.ToUpperInvariant(driveLetter)}:'"))
            using (var results = volumes.Get())
            {
                foreach (ManagementObject v in results)
                    using (v) volumeGuid ??= VolumeGuidOf(v["DeviceID"] as string);
            }

            if (volumeGuid is null) return null;

            using var storage = new ManagementObjectSearcher(@"root\cimv2", "SELECT UsedSpace, Volume FROM Win32_ShadowStorage");
            using var rows = storage.Get();
            long total = 0;
            foreach (ManagementObject s in rows)
            {
                using (s)
                {
                    if (!string.Equals(VolumeGuidOf(s["Volume"] as string), volumeGuid, StringComparison.OrdinalIgnoreCase)) continue;
                    total += Convert.ToInt64(s["UsedSpace"] ?? 0UL);
                }
            }

            return total;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Extracts "Volume{guid}" from a device ID or a WMI reference such as Win32_Volume.DeviceID="\\\\?\\Volume{…}\\".</summary>
    internal static string? VolumeGuidOf(string? text)
    {
        if (text is null) return null;
        int start = text.IndexOf("Volume{", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        int end = text.IndexOf('}', start);
        return end < 0 ? null : text.Substring(start, end - start + 1);
    }
}
