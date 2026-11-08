using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace DiskLens.Core.Services;

public sealed record RevoInstallation(string ExePath, bool IsPro, string Version);

/// <summary>Finds Revo Uninstaller (free or Pro) on this PC.</summary>
public static class RevoLocator
{
    private static readonly string[] ExeNames = { "RevoUninPro.exe", "RevoUnin.exe" };

    /// <param name="installedApps">The installed programs list, if already loaded (saves reading the registry twice).</param>
    public static RevoInstallation? Find(IEnumerable<InstalledApp>? installedApps = null)
    {
        foreach (var path in CandidatePaths(installedApps))
        {
            try
            {
                // DiskLens runs as administrator and so would Revo: only trust a copy in Program Files, which
                // other software can't replace (a per-user registry entry could point anywhere).
                if (!TrustedPrograms.IsInProtectedFolder(path) || !File.Exists(path)) continue;
                bool isPro = Path.GetFileName(path).Equals("RevoUninPro.exe", StringComparison.OrdinalIgnoreCase);
                string version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
                return new RevoInstallation(path, isPro, version);
            }
            catch (IOException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths(IEnumerable<InstalledApp>? installedApps)
    {
        // 1. "App Paths" registration
        foreach (var exe in ExeNames)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                string? registered = ReadValue(RegistryHive.LocalMachine, view, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}", null);
                if (!string.IsNullOrWhiteSpace(registered)) yield return registered.Trim('"');
            }
        }

        // 2. Its own entry in the installed programs list
        foreach (var app in (installedApps ?? SafeLoadApps()).Where(a => a.Name.StartsWith("Revo Uninstaller", StringComparison.OrdinalIgnoreCase)))
        {
            if (app.InstallLocation is null) continue;
            foreach (var exe in ExeNames) yield return Path.Combine(app.InstallLocation, exe);
        }

        // 3. Default install folders
        foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(programFiles)) continue;
            yield return Path.Combine(programFiles, "VS Revo Group", "Revo Uninstaller Pro", "RevoUninPro.exe");
            yield return Path.Combine(programFiles, "VS Revo Group", "Revo Uninstaller", "RevoUnin.exe");
        }
    }

    private static List<InstalledApp> SafeLoadApps()
    {
        try { return InstalledAppsService.Load(); }
        catch { return new List<InstalledApp>(); }
    }

    private static string? ReadValue(RegistryHive hive, RegistryView view, string keyPath, string? valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
