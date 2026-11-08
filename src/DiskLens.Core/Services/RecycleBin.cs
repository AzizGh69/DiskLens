using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DiskLens.Core.Services;

public enum RecycleOutcome
{
    /// <summary>The item is gone from its folder and is in the Recycle Bin.</summary>
    Moved,

    /// <summary>The person cancelled, or some files were in use and stayed where they were.</summary>
    NotMoved,

    /// <summary>A folder was only partly moved (some files were in use).</summary>
    Partial,
}

/// <summary>Moves files and folders to the Recycle Bin with the Windows shell, so they can be restored.</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// Sends <paramref name="path"/> to the Recycle Bin. The caller asks for confirmation first. If an item is
    /// too big for the Recycle Bin, Windows itself warns before deleting it permanently.
    /// </summary>
    /// <param name="owner">Window handle that owns Windows' progress and warning dialogs.</param>
    public static RecycleOutcome Move(string path, IntPtr owner)
    {
        bool wasFolder = Directory.Exists(path);
        if (!wasFolder && !File.Exists(path)) return RecycleOutcome.Moved; // already gone

        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING,
        };

        int code = SHFileOperation(ref op);
        bool stillThere = wasFolder ? Directory.Exists(path) : File.Exists(path);
        if (!stillThere) return RecycleOutcome.Moved;
        if (code != 0 || op.fAnyOperationsAborted) return wasFolder && FolderChanged(path) ? RecycleOutcome.Partial : RecycleOutcome.NotMoved;
        return wasFolder ? RecycleOutcome.Partial : RecycleOutcome.NotMoved;
    }

    private static bool FolderChanged(string path)
    {
        // Can't know exactly what moved; a rescan gives the real numbers. Treat any folder that still exists as partial.
        try { return Directory.Exists(path); }
        catch { return true; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
