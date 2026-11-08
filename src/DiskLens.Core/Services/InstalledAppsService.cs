using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskLens.Core.Models;
using Microsoft.Win32;

namespace DiskLens.Core.Services;

public sealed class InstalledApp
{
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";

    /// <summary>Folder the app lives in, if it could be determined.</summary>
    public string? InstallLocation { get; init; }

    /// <summary>True when the location came from the app's own InstallLocation entry (not guessed from its uninstaller or icon).</summary>
    public bool LocationIsReliable { get; init; }

    /// <summary>Size the installer reported, in bytes (often missing or out of date).</summary>
    public long EstimatedSize { get; init; }

    public string? UninstallString { get; init; }
    public bool IsStoreApp { get; init; }

    public char? DriveLetter => InstallLocation is { Length: > 1 } loc && loc[1] == ':' ? char.ToUpperInvariant(loc[0]) : null;
}

/// <summary>An installed app with its size on one drive.</summary>
public sealed class AppSize
{
    public AppSize(InstalledApp app, long size, bool isEstimate, FolderNode? folder)
    {
        App = app;
        Size = size;
        IsEstimate = isEstimate;
        Folder = folder;
    }

    public InstalledApp App { get; }
    public long Size { get; }

    /// <summary>The size comes from the installer's estimate instead of measuring the folder.</summary>
    public bool IsEstimate { get; }
    public FolderNode? Folder { get; }
}

/// <summary>Reads installed programs from the registry (the same list as "Apps &amp; features") and measures their folders.</summary>
public static class InstalledAppsService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledApp> Load()
    {
        var apps = new List<InstalledApp>();
        ReadUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, apps);
        ReadUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry32, apps);
        ReadUninstallKeys(RegistryHive.CurrentUser, RegistryView.Default, apps);

        // The same app can appear under several keys; keep one entry per name + location.
        return apps
            .GroupBy(a => (a.Name.ToLowerInvariant(), (a.InstallLocation ?? "").ToLowerInvariant()))
            .Select(g => g.OrderByDescending(a => a.EstimatedSize).First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void ReadUninstallKeys(RegistryHive hive, RegistryView view, List<InstalledApp> apps)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallKey);
            if (uninstall is null) return;

            foreach (string subName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var key = uninstall.OpenSubKey(subName);
                    if (key is null) continue;
                    var app = ReadApp(key);
                    if (app is not null) apps.Add(app);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private static InstalledApp? ReadApp(RegistryKey key)
    {
        string name = (key.GetValue("DisplayName") as string)?.Trim() ?? "";
        if (name.Length == 0) return null;
        if (key.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (key.GetValue("ParentKeyName") is string) return null; // updates / patches of another app
        string releaseType = key.GetValue("ReleaseType") as string ?? "";
        if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) return null;

        string? uninstall = key.GetValue("UninstallString") as string;
        string? location = NormalizeLocation(key.GetValue("InstallLocation") as string);
        bool reliable = location is not null && IsUsableLocation(location);
        if (!reliable)
        {
            location = new[]
            {
                FolderOfCommand(key.GetValue("DisplayIcon") as string),
                FolderOfCommand(uninstall),
            }.Select(GuessAppFolder).FirstOrDefault(l => l is not null && IsUsableLocation(l));
        }

        long estimatedKb = key.GetValue("EstimatedSize") is int kb && kb > 0 ? kb : 0;

        return new InstalledApp
        {
            Name = name,
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "",
            Version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            InstallLocation = location,
            LocationIsReliable = reliable,
            EstimatedSize = estimatedKb * 1024,
            UninstallString = uninstall,
        };
    }

    internal static string? NormalizeLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string s = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"').Trim());
        if (s.Length < 3 || s[1] != ':') return null;
        try
        {
            string full = Path.GetFullPath(s);
            return full.Length > 3 ? full.TrimEnd('\\') : full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The folder of the program in a command line such as <c>"C:\App\uninst.exe" /S</c> or <c>C:\App\app.exe,0</c>.</summary>
    internal static string? FolderOfCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string s = command.Trim();
        string exe;
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return null;
            exe = s.Substring(1, end - 1);
        }
        else
        {
            int comma = s.IndexOf(',');
            if (comma > 0) s = s.Substring(0, comma);
            int dotExe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exe = dotExe > 0 ? s.Substring(0, dotExe + 4) : s;
        }

        if (exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return null;
        return NormalizeLocation(Path.GetDirectoryName(exe));
    }

    private static readonly HashSet<string> HelperFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "x64", "x86", "uninstall", "uninstaller", "uninst", "_uninst",
    };

    /// <summary>
    /// A program's icon or uninstaller often sits in a helper subfolder (<c>App\bin\app.exe</c>,
    /// <c>App\uninstall\unins.exe</c>); the app itself is the folder above it.
    /// </summary>
    internal static string? GuessAppFolder(string? folder)
    {
        if (folder is null || !HelperFolderNames.Contains(Path.GetFileName(folder))) return folder;
        string? parent = Path.GetDirectoryName(folder);
        return parent is not null && IsUsableLocation(parent) ? parent : folder;
    }

    /// <summary>Rejects folders that are shared or belong to Windows / installer caches rather than to one app.</summary>
    internal static bool IsUsableLocation(string folder)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (folder.Length <= 3) return false;
        if (folder.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return false;

        string name = Path.GetFileName(folder);
        string parent = Path.GetDirectoryName(folder) ?? "";
        if (parent.Length <= 3 && (name.StartsWith("Program Files", StringComparison.OrdinalIgnoreCase) ||
                                   name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
                                   name.Equals("Users", StringComparison.OrdinalIgnoreCase)))
            return false;

        string[] sharedFolders = { @"\Common Files", @"\Package Cache\", @"\InstallShield Installation Information\", @"\Installer\", @"\Uninstall Information" };
        if (sharedFolders.Any(s => (folder + "\\").Contains(s + (s.EndsWith('\\') ? "" : "\\"), StringComparison.OrdinalIgnoreCase)))
            return false;
        if (name.Equals("Common Files", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    /// <summary>
    /// Sizes for the apps that live on the scanned drive. Folder sizes are measured from the scan; an app
    /// folder that contains another app's folder (e.g. a game launcher and its games) only counts its own part.
    /// </summary>
    public static List<AppSize> Measure(ScanResult scan, IEnumerable<InstalledApp> apps, bool isWindowsDrive)
    {
        char? drive = scan.RootPath.Length > 1 && scan.RootPath[1] == ':' ? char.ToUpperInvariant(scan.RootPath[0]) : null;
        var located = new List<(InstalledApp App, FolderNode Node)>();
        var estimated = new List<AppSize>();

        foreach (var app in apps)
        {
            if (app.DriveLetter is char letter)
            {
                if (letter != drive) continue;
                var node = scan.FindFolder(app.InstallLocation!);
                if (node is not null && !node.IsRoot)
                {
                    located.Add((app, node));
                    continue;
                }
            }
            else if (!isWindowsDrive)
            {
                continue; // apps without a known folder are listed on the Windows drive only
            }

            if (app.EstimatedSize > 0) estimated.Add(new AppSize(app, app.EstimatedSize, true, null));
        }

        located.AddRange(StoreApps(scan));

        // Several entries can point at the same folder (e.g. Office and its language packs): the folder is
        // counted once, for the entry that named it directly; the others fall back to their own estimate.
        var shared = located
            .GroupBy(l => l.Node)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderByDescending(l => l.App.LocationIsReliable).ThenByDescending(l => l.App.EstimatedSize).Skip(1))
            .ToList();
        foreach (var extra in shared)
        {
            located.Remove(extra);
            if (extra.App.EstimatedSize > 0) estimated.Add(new AppSize(extra.App, extra.App.EstimatedSize, true, null));
        }

        // Subtract app folders nested inside another app's folder from that outer app.
        var appNodes = new HashSet<FolderNode>(located.Select(l => l.Node));
        var nestedBytes = new Dictionary<FolderNode, long>();
        foreach (var node in appNodes)
        {
            for (var p = node.Parent; p is not null; p = p.Parent)
            {
                if (!appNodes.Contains(p)) continue;
                nestedBytes[p] = nestedBytes.GetValueOrDefault(p) + node.Size;
                break;
            }
        }

        var result = new List<AppSize>(estimated);
        foreach (var (app, node) in located)
        {
            long measured = Math.Max(0, node.Size - nestedBytes.GetValueOrDefault(node));
            bool useEstimate = !app.LocationIsReliable && !app.IsStoreApp && app.EstimatedSize > 0 && measured < app.EstimatedSize / 2;
            result.Add(useEstimate
                ? new AppSize(app, app.EstimatedSize, true, node)
                : new AppSize(app, measured, false, node));
        }

        return result.OrderByDescending(a => a.Size).ToList();
    }

    /// <summary>Microsoft Store apps, grouped by package from Program Files\WindowsApps (when it could be read).</summary>
    private static IEnumerable<(InstalledApp, FolderNode)> StoreApps(ScanResult scan)
    {
        var windowsApps = scan.Root.FindChild("Program Files")?.FindChild("WindowsApps");
        if (windowsApps is null) yield break;

        foreach (var package in windowsApps.Children)
        {
            if (package.Size < 50L * 1024 * 1024) continue;
            string id = package.Name;
            int underscore = id.IndexOf('_');
            string familyName = underscore > 0 ? id.Substring(0, underscore) : id;
            yield return (new InstalledApp
            {
                Name = familyName,
                Publisher = "Microsoft Store app",
                Version = underscore > 0 ? id.Substring(underscore + 1).Split('_')[0] : "",
                InstallLocation = package.FullPath,
                LocationIsReliable = true,
                IsStoreApp = true,
            }, package);
        }
    }
}
