using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiskLens.Core.Services;

/// <summary>
/// Decides what may be moved to the Recycle Bin. Anything Windows or an installed app needs is refused,
/// with a reason the person can read.
/// </summary>
public sealed class RecycleGuard
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> WindowsRootFolders = new(Ci)
    {
        "Windows", "System Volume Information", "Recovery", "Boot", "EFI", "$WinREAgent", "$SysReset",
        "$Windows.~BT", "$Windows.~WS", "$GetCurrent", "Config.Msi", "Documents and Settings", "MSOCache",
    };

    private static readonly HashSet<string> WindowsRootFiles = new(Ci)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp",
        "bootmgr", "BOOTNXT", "BOOTSECT.BAK",
    };

    private static readonly HashSet<string> MainUserFolders = new(Ci)
    {
        "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music", "Favorites", "Contacts",
        "Links", "Saved Games", "Searches", "3D Objects", "OneDrive",
    };

    private readonly string _root;
    private readonly bool _hasRecycleBin;
    private readonly string _windowsDir;
    private readonly List<(string Folder, string App)> _apps;

    /// <param name="driveRoot">The drive the items are on, e.g. <c>C:\</c>.</param>
    /// <param name="hasRecycleBin">False for FAT32 / removable drives, where deleting is permanent.</param>
    /// <param name="appFolders">Install folders of installed apps (any drive).</param>
    /// <param name="windowsDir">The Windows folder; defaults to this PC's.</param>
    public RecycleGuard(string driveRoot, bool hasRecycleBin, IEnumerable<(string Folder, string App)> appFolders, string? windowsDir = null)
    {
        _root = driveRoot.TrimEnd('\\') + "\\";
        _hasRecycleBin = hasRecycleBin;
        _windowsDir = (windowsDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');
        _apps = appFolders
            .Where(a => !string.IsNullOrWhiteSpace(a.Folder) && a.Folder.TrimEnd('\\').Length > 3)
            .Select(a => (a.Folder.TrimEnd('\\'), a.App))
            .ToList();
    }

    /// <summary>Why <paramref name="path"/> can't go to the Recycle Bin, or null if it can.</summary>
    public string? BlockReason(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "This item has no location on disk.";

        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); }
        catch { return "This path isn't valid."; }

        if (!(full + "\\").StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return "This item isn't on this drive.";

        string relative = (full + "\\").Length > _root.Length ? full.Substring(_root.Length) : "";
        if (relative.Length == 0) return "This is the whole drive.";

        if (!_hasRecycleBin)
            return "This drive has no Recycle Bin (FAT32 or removable), so deleting here would be permanent. DiskLens only moves things to the Recycle Bin.";

        if (IsSameOrInside(full, _windowsDir))
            return "Part of Windows. Removing it can stop Windows from working.";

        string[] parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string top = parts[0];

        if (top.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
            return "This is the Recycle Bin itself. Empty it from the desktop instead.";
        if (top.Equals("Windows.old", StringComparison.OrdinalIgnoreCase))
            return "Your previous Windows installation. Remove it with Disk Cleanup (see the System tab).";
        if (WindowsRootFolders.Contains(top))
            return "Windows needs this folder.";
        if (parts.Length == 1 && WindowsRootFiles.Contains(top))
            return "Windows needs this file. The System tab explains safe ways to make it smaller.";

        // Installed apps: the app's folder, anything inside it, or a folder that contains apps
        foreach (var (folder, app) in _apps)
        {
            if (IsSameOrInside(full, folder))
                return $"Part of \"{app}\". Uninstall it with Revo instead, so nothing is left half-removed.";
        }

        var contained = _apps.Where(a => IsSameOrInside(a.Folder, full)).Select(a => a.App).Distinct().ToList();
        if (contained.Count > 0)
        {
            string names = contained.Count == 1 ? $"\"{contained[0]}\"" : $"\"{contained[0]}\" and {contained.Count - 1} more";
            return $"Contains installed apps ({names}). Uninstall them with Revo instead.";
        }

        if (top.Equals("Program Files", StringComparison.OrdinalIgnoreCase) || top.StartsWith("Program Files (", StringComparison.OrdinalIgnoreCase))
            return "Inside Program Files. Uninstall the app with Revo instead of deleting its files.";

        if (top.Equals("ProgramData", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 1) return "Windows and apps keep shared settings here.";
            if (parts[1].Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("Package Cache", StringComparison.OrdinalIgnoreCase))
                return "Windows and installers need this folder.";
        }

        if (top.Equals("Users", StringComparison.OrdinalIgnoreCase))
            return UserFolderReason(parts);

        return null;
    }

    private static string? UserFolderReason(string[] parts)
    {
        if (parts.Length == 1) return "This holds every account's files.";
        if (parts.Length == 2) return "This is a whole user account.";
        if (parts[1].Equals("Default", StringComparison.OrdinalIgnoreCase)) return "Windows uses this as the template for new accounts.";

        string third = parts[2];
        if (parts.Length == 3 && (MainUserFolders.Contains(third) || third.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)))
            return "This is one of your main folders. Open it and remove what's inside instead.";

        if (third.Equals("AppData", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length <= 4) return "Apps keep their settings here.";
            string area = parts[3];
            string name = parts[4];
            if (name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                (parts.Length == 5 || parts[5].Equals("Windows", StringComparison.OrdinalIgnoreCase)))
                return "Windows keeps settings for your account here.";
            if (parts.Length == 5 && area.Equals("Local", StringComparison.OrdinalIgnoreCase) &&
                (name.Equals("Packages", StringComparison.OrdinalIgnoreCase) || name.Equals("Programs", StringComparison.OrdinalIgnoreCase) || name.Equals("Temp", StringComparison.OrdinalIgnoreCase)))
                return name.Equals("Temp", StringComparison.OrdinalIgnoreCase)
                    ? "Apps may be using files in here. Remove what's inside, or use Storage settings → Temporary files."
                    : "Apps are installed in here. Uninstall them with Revo instead.";
        }

        return null;
    }

    private static bool IsSameOrInside(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
}
