using System;
using System.Collections.Generic;
using System.Linq;
using DiskLens.Core.Models;

namespace DiskLens.Core.Services;

public enum SpaceCategory
{
    System,
    Apps,
    User,
    Other,
}

/// <summary>How a drive's capacity splits up. All values in bytes; they add up to the drive size.</summary>
public sealed class CategoryBreakdown
{
    public long System { get; internal set; }
    public long Apps { get; internal set; }
    public long User { get; internal set; }
    public long Other { get; internal set; }

    /// <summary>Used space the scan couldn't see: protected folders, restore points, file-system metadata.</summary>
    public long Unreadable { get; internal set; }
    public long Free { get; internal set; }

    public long Total => System + Apps + User + Other + Unreadable + Free;

    public long this[SpaceCategory category] => category switch
    {
        SpaceCategory.System => System,
        SpaceCategory.Apps => Apps,
        SpaceCategory.User => User,
        _ => Other,
    };

    internal void Add(SpaceCategory category, long bytes)
    {
        switch (category)
        {
            case SpaceCategory.System: System += bytes; break;
            case SpaceCategory.Apps: Apps += bytes; break;
            case SpaceCategory.User: User += bytes; break;
            default: Other += bytes; break;
        }
    }
}

/// <summary>Sorts scanned space into System / Apps &amp; app data / Your files / Other, and finds the heaviest folders.</summary>
public static class Categorizer
{
    private static readonly HashSet<string> SystemRootFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "System Volume Information", "$Recycle.Bin", "Recovery", "Boot", "EFI", "$WinREAgent",
        "$Windows.~BT", "$Windows.~WS", "Windows.old", "$SysReset", "PerfLogs", "Config.Msi", "$GetCurrent",
    };

    private static readonly HashSet<string> AppRootFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files", "Program Files (x86)", "Program Files (Arm)", "ProgramData", "MSOCache",
        "XboxGames", "SteamLibrary", "Epic Games", "Riot Games", "GOG Games",
    };

    private static readonly HashSet<string> SystemRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "bootmgr", "BOOTNXT", "BOOTSECT.BAK",
    };

    private static readonly HashSet<string> SystemProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default", "Default User", "All Users", "defaultuser0", "defaultuser100000",
    };

    /// <summary>Folders that only group other things, so the "heaviest folder" answer looks inside them.</summary>
    private static readonly HashSet<string> GameLibraryFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "SteamLibrary", "steamapps", "XboxGames", "Epic Games", "Riot Games", "GOG Games",
    };

    public static bool IsSystemRootFile(string name) => SystemRootFiles.Contains(name);

    /// <summary>Category decided by the folder's own position, or null to inherit from its parent.</summary>
    internal static SpaceCategory? RuleFor(FolderNode node, int depth)
    {
        if (depth == 1)
        {
            if (SystemRootFolders.Contains(node.Name)) return SpaceCategory.System;
            if (AppRootFolders.Contains(node.Name)) return SpaceCategory.Apps;
            if (node.Name.Equals("Users", StringComparison.OrdinalIgnoreCase)) return SpaceCategory.User;
            return null;
        }

        if (depth == 2 && IsUsersFolder(node.Parent))
            return SystemProfiles.Contains(node.Name) ? SpaceCategory.System : SpaceCategory.User;

        if (depth == 3 && IsUsersFolder(node.Parent?.Parent) && node.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
            return SpaceCategory.Apps;

        return null;
    }

    public static CategoryBreakdown Compute(ScanResult scan, long driveUsed, long driveFree, IEnumerable<FolderNode>? appFolders = null)
    {
        var breakdown = new CategoryBreakdown();
        var apps = appFolders is null ? new HashSet<FolderNode>() : new HashSet<FolderNode>(appFolders);

        foreach (var file in scan.RootFiles)
            breakdown.Add(IsSystemRootFile(file.Name) ? SpaceCategory.System : SpaceCategory.Other, file.Size);

        var stack = new Stack<(FolderNode Node, int Depth, SpaceCategory Inherited)>();
        foreach (var child in scan.Root.Children) stack.Push((child, 1, SpaceCategory.Other));

        while (stack.Count > 0)
        {
            var (node, depth, inherited) = stack.Pop();
            var category = RuleFor(node, depth) ?? (apps.Contains(node) ? SpaceCategory.Apps : inherited);

            if (category is SpaceCategory.System or SpaceCategory.Apps)
            {
                breakdown.Add(category, node.Size);
                continue;
            }

            // Your files / Other can still contain app data or app install folders deeper down.
            breakdown.Add(category, node.OwnFilesSize);
            foreach (var child in node.Children) stack.Push((child, depth + 1, category));
        }

        breakdown.Unreadable = Math.Max(0, driveUsed - scan.TotalBytes);
        breakdown.Free = Math.Max(0, driveFree);
        return breakdown;
    }

    /// <summary>
    /// The heaviest folders that mean something to a person: looks inside grouping folders
    /// (Users, Program Files, AppData, game libraries, …) and leaves out Windows' own folders.
    /// </summary>
    public static List<FolderNode> HeaviestFolders(ScanResult scan, int count = 3)
    {
        var candidates = new List<FolderNode>();
        var stack = new Stack<(FolderNode Node, int Depth)>();
        foreach (var child in scan.Root.Children) stack.Push((child, 1));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (IsSystemFolder(node, depth)) continue;

            if (IsGroupingFolder(node, depth))
            {
                foreach (var child in node.Children) stack.Push((child, depth + 1));
                continue;
            }

            candidates.Add(node);
        }

        return candidates.Where(c => c.Size > 0).OrderByDescending(c => c.Size).Take(count).ToList();
    }

    private static bool IsSystemFolder(FolderNode node, int depth) =>
        RuleFor(node, depth) == SpaceCategory.System;

    private static bool IsGroupingFolder(FolderNode node, int depth)
    {
        string name = node.Name;
        if (depth == 1 && (name.Equals("Users", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Program Files", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (depth == 2 && IsUsersFolder(node.Parent)) return true;                       // C:\Users\<you>
        if (depth == 3 && IsUsersFolder(node.Parent?.Parent) && name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) return true;
        if (depth == 4 && node.Parent?.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase) == true) return true; // Local, Roaming, LocalLow
        if (GameLibraryFolders.Contains(name)) return true;
        if (name.Equals("common", StringComparison.OrdinalIgnoreCase) &&
            node.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true) return true;
        return false;
    }

    private static bool IsUsersFolder(FolderNode? node) =>
        node is not null && node.Parent is { IsRoot: true } && node.Name.Equals("Users", StringComparison.OrdinalIgnoreCase);
}
