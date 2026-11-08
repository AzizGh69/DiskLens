using System;
using System.Collections.Generic;
using System.IO;

namespace DiskLens.Core.Models;

/// <summary>A folder in the scanned tree. Sizes include everything below it.</summary>
public sealed class FolderNode
{
    private static readonly IReadOnlyList<FolderNode> NoChildren = Array.Empty<FolderNode>();

    internal FolderNode(string name, FolderNode? parent)
    {
        Name = name;
        Parent = parent;
    }

    public string Name { get; }
    public FolderNode? Parent { get; private set; }

    internal List<FolderNode>? ChildList { get; set; }
    public IReadOnlyList<FolderNode> Children => ChildList ?? NoChildren;

    /// <summary>Total bytes of all files in this folder and its subfolders.</summary>
    public long Size { get; internal set; }

    /// <summary>Total number of files in this folder and its subfolders.</summary>
    public long FileCount { get; internal set; }

    /// <summary>Bytes of the files directly inside this folder.</summary>
    public long OwnFilesSize { get; internal set; }
    public long OwnFileCount { get; internal set; }

    /// <summary>Windows refused to list this folder (even as administrator).</summary>
    public bool Unreadable { get; internal set; }

    public bool IsRoot => Parent is null;

    public string FullPath => Parent is null ? Name : Path.Join(Parent.FullPath, Name);

    public int Depth
    {
        get
        {
            int depth = 0;
            for (var p = Parent; p is not null; p = p.Parent) depth++;
            return depth;
        }
    }

    public FolderNode? FindChild(string name)
    {
        if (ChildList is null) return null;
        foreach (var child in ChildList)
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    /// <summary>Removes this folder from the tree and subtracts its size from every parent.</summary>
    internal void Detach()
    {
        if (Parent is null) return;
        Parent.ChildList?.Remove(this);
        for (var p = Parent; p is not null; p = p.Parent)
        {
            p.Size -= Size;
            p.FileCount -= FileCount;
        }

        Parent = null;
    }

    /// <summary>Subtracts a removed file from this folder and every parent.</summary>
    internal void RemoveOwnFile(long size)
    {
        OwnFilesSize -= size;
        OwnFileCount--;
        for (var p = this; p is not null; p = p.Parent)
        {
            p.Size -= size;
            p.FileCount--;
        }
    }

    public override string ToString() => $"{FullPath} ({Size} bytes)";
}

/// <summary>A single file found during a scan.</summary>
public sealed class FileEntry
{
    public FileEntry(string fullPath, long size, DateTime lastWriteUtc)
    {
        FullPath = fullPath;
        Size = size;
        LastWriteUtc = lastWriteUtc;
    }

    public string FullPath { get; }
    public long Size { get; }
    public DateTime LastWriteUtc { get; }

    public string Name => Path.GetFileName(FullPath);
    public string Folder => Path.GetDirectoryName(FullPath) ?? "";
    public string Extension => Path.GetExtension(FullPath).TrimStart('.').ToLowerInvariant();
}

public sealed class ScanResult
{
    public ScanResult(string rootPath, FolderNode root)
    {
        RootPath = rootPath;
        Root = root;
    }

    public string RootPath { get; }
    public FolderNode Root { get; }

    /// <summary>The largest files on the drive, biggest first.</summary>
    public List<FileEntry> LargestFiles { get; } = new();

    /// <summary>Files directly in the drive root (pagefile.sys, hiberfil.sys, ...).</summary>
    public List<FileEntry> RootFiles { get; } = new();

    public long FileCount { get; internal set; }
    public long FolderCount { get; internal set; }
    public int UnreadableCount { get; internal set; }
    public List<string> UnreadableSamples { get; } = new();
    public TimeSpan Duration { get; internal set; }
    public DateTime FinishedAt { get; internal set; }

    public long TotalBytes => Root.Size;

    /// <summary>Finds a folder by full path, or null if it isn't in this scan.</summary>
    public FolderNode? FindFolder(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;
        string normalized;
        try { normalized = Path.GetFullPath(fullPath).TrimEnd('\\'); }
        catch { return null; }

        string root = RootPath.TrimEnd('\\');
        if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)) return Root;
        if (!normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return null;

        var node = Root;
        foreach (var part in normalized.Substring(root.Length + 1).Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.FindChild(part);
            if (node is null) return null;
        }

        return node;
    }
}

public readonly record struct ScanProgress(long Files, long Bytes, long Folders, int Unreadable, string CurrentFolder);
