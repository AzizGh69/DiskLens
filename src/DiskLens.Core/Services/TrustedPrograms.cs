using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiskLens.Core.Services;

/// <summary>
/// DiskLens runs as administrator, so every program it starts runs as administrator too. Programs are only
/// started from folders that need administrator rights to change (Windows, Program Files), so other software
/// on the PC can't plant a look-alike program for DiskLens to launch.
/// </summary>
public static class TrustedPrograms
{
    private static readonly string[] AllowedUriSchemes = { "ms-settings:" };

    /// <summary>Folders only administrators can write to.</summary>
    public static IReadOnlyList<string> ProtectedFolders { get; } = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
        }
        .Where(f => f.Length > 3)
        .Select(f => f.TrimEnd('\\'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>True if <paramref name="path"/> is inside Windows or Program Files.</summary>
    public static bool IsInProtectedFolder(string path, IEnumerable<string>? protectedFolders = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return false; }
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false; // network / device paths

        return (protectedFolders ?? ProtectedFolders).Any(folder =>
            full.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The full path of a program DiskLens may start, or null if it isn't allowed. A bare name such as
    /// <c>cleanmgr.exe</c> is looked up in System32 and the Windows folder only (never the current folder);
    /// a full path must be inside Windows or Program Files; a URI must be an <c>ms-settings:</c> page.
    /// </summary>
    public static string? Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        if (AllowedUriSchemes.Any(s => fileName.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            return fileName.All(c => char.IsLetterOrDigit(c) || c is ':' or '-' or '_' or '.') ? fileName : null;

        if (Path.IsPathFullyQualified(fileName))
            return IsInProtectedFolder(fileName) && File.Exists(fileName) ? Path.GetFullPath(fileName) : null;

        if (fileName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) return null; // relative paths

        foreach (var folder in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
        {
            string candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
