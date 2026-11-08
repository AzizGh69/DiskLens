using System;
using System.Collections.Generic;
using DiskLens.Core.Models;
using DiskLens.Core.Services;

namespace DiskLens.Tests;

/// <summary>Builds an in-memory scan result for testing, e.g. Folder(@"Users\me\Documents", 20).</summary>
internal sealed class TestTree
{
    private readonly FolderNode _root = new(@"C:\", null);
    private readonly ScanResult _result;

    public TestTree() => _result = new ScanResult(@"C:\", _root);

    public TestTree Folder(string relativePath, long ownBytes)
    {
        GetOrCreate(relativePath).OwnFilesSize += ownBytes;
        return this;
    }

    public TestTree RootFile(string name, long bytes)
    {
        _result.RootFiles.Add(new FileEntry(@"C:\" + name, bytes, DateTime.UtcNow));
        _root.OwnFilesSize += bytes;
        return this;
    }

    public FolderNode Node(string relativePath) => _result.FindFolder(@"C:\" + relativePath)!;

    public ScanResult Build()
    {
        Scanner.ComputeTotals(_root);
        return _result;
    }

    private FolderNode GetOrCreate(string relativePath)
    {
        var node = _root;
        foreach (var part in relativePath.Split('\\'))
        {
            var child = node.FindChild(part);
            if (child is null)
            {
                child = new FolderNode(part, node);
                (node.ChildList ??= new List<FolderNode>()).Add(child);
            }

            node = child;
        }

        return node;
    }
}
