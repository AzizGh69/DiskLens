using System.Globalization;
using DiskLens.Core;
using DiskLens.Core.Models;
using DiskLens.Core.Services;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

/// <summary>Anything in a list that lives at a path: a file, a folder, an app's folder or a system item.</summary>
public interface IPathItem
{
    string FullPath { get; }
    string Name { get; }
    long Size { get; }
    bool IsFolder { get; }

    /// <summary>Why this item can't be moved to the Recycle Bin, or null if it can.</summary>
    string? BlockReason { get; }
}

public abstract class PathItemViewModel : ObservableObject, IPathItem
{
    private string? _blockReason;

    public abstract string FullPath { get; }
    public abstract string Name { get; }
    public abstract long Size { get; }
    public abstract bool IsFolder { get; }

    public string? BlockReason
    {
        get => _blockReason;
        set
        {
            if (SetProperty(ref _blockReason, value)) OnPropertyChanged(nameof(CanRecycle));
        }
    }

    public bool CanRecycle => BlockReason is null;
}

/// <summary>A row in "Largest files".</summary>
public sealed class FileRowViewModel : PathItemViewModel
{
    public FileRowViewModel(FileEntry entry, long largest)
    {
        Entry = entry;
        Group = FileTypes.GroupOf(entry.Extension);
        Glyph = FileTypes.GlyphOf(Group);
        Percent = Format.Share(entry.Size, largest);
    }

    public FileEntry Entry { get; }
    public override string FullPath => Entry.FullPath;
    public override string Name => Entry.Name;
    public override long Size => Entry.Size;
    public override bool IsFolder => false;

    public string Folder => Entry.Folder;
    public string SizeText => Format.Bytes(Entry.Size);
    public string Group { get; }
    public string Glyph { get; }
    public double Percent { get; }
    public string ModifiedText => Entry.LastWriteUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}

/// <summary>A row in the Folders tab: a subfolder or a file in the current folder.</summary>
public sealed class FolderItemViewModel : PathItemViewModel
{
    private readonly string _fullPath;
    private readonly string _name;
    private readonly long _size;

    public FolderItemViewModel(FolderNode node, long parentSize)
    {
        Node = node;
        _fullPath = node.FullPath;
        _name = node.Name;
        _size = node.Size;
        Percent = Format.Share(node.Size, parentSize);
        Glyph = node.Unreadable ? "" : "";
        Details = node.Unreadable
            ? "Locked by Windows — some contents couldn't be read"
            : node.FileCount == 1 ? "1 file" : $"{Format.Count(node.FileCount)} files";
    }

    public FolderItemViewModel(FileEntry file, long parentSize)
    {
        File = file;
        _fullPath = file.FullPath;
        _name = file.Name;
        _size = file.Size;
        Percent = Format.Share(file.Size, parentSize);
        Glyph = FileTypes.GlyphOf(FileTypes.GroupOf(file.Extension));
        Details = FileTypes.GroupOf(file.Extension) is var g && g != FileTypes.Other ? g : "File";
    }

    public FolderNode? Node { get; }
    public FileEntry? File { get; }

    public override string FullPath => _fullPath;
    public override string Name => _name;
    public override long Size => _size;
    public override bool IsFolder => Node is not null;

    public string Glyph { get; }
    public string SizeText => Format.Bytes(_size);
    public double Percent { get; }
    public string PercentText => Format.Percent(Percent);
    public string Details { get; }
}

/// <summary>A row in the Apps tab.</summary>
public sealed class AppRowViewModel : PathItemViewModel
{
    public AppRowViewModel(AppSize size, long largest)
    {
        AppSize = size;
        Percent = Format.Share(size.Size, largest);
        BlockReason = "Apps should be removed with an uninstaller (Revo), not by deleting their folder.";
    }

    public AppSize AppSize { get; }
    public InstalledApp App => AppSize.App;

    public override string FullPath => App.InstallLocation ?? "";
    public override string Name => App.Name;
    public override long Size => AppSize.Size;
    public override bool IsFolder => true;

    public FolderNode? Folder => AppSize.Folder;
    public bool HasLocation => App.InstallLocation is not null;
    public string Glyph => App.IsStoreApp ? "" : "";

    /// <summary>"≈" marks the installer's own estimate (the folder wasn't measured).</summary>
    public string SizeText => AppSize.IsEstimate ? "≈ " + Format.Bytes(Size) : Format.Bytes(Size);
    public string SizeToolTip => AppSize.IsEstimate
        ? "Estimate reported by the app's installer — its folder couldn't be measured."
        : "Measured from the scan of its folder.";

    public double Percent { get; }
    public string Publisher => App.Publisher;
    public string Version => App.Version;
    public string Location => App.InstallLocation ?? "Folder not recorded by the installer";
}

/// <summary>A card in the System tab.</summary>
public sealed class SystemItemViewModel : PathItemViewModel
{
    public SystemItemViewModel(SystemItem item, long largest)
    {
        Item = item;
        Percent = Format.Share(item.Size, largest);
        BlockReason = "Windows manages this. Use the Windows tool on its card instead of deleting it.";
    }

    public SystemItem Item { get; }

    public override string FullPath => Item.Path ?? "";
    public override string Name => Item.Name;
    public override long Size => Item.Size;
    public override bool IsFolder => Item.IsFolder;

    public string SizeText => Format.Bytes(Item.Size);
    public double Percent { get; }
    public string WhatItIs => Item.WhatItIs;
    public string Tip => Item.Tip;
    public string? SizeNote => Item.SizeNote;
    public bool HasSizeNote => Item.SizeNote is not null;
    public bool HasPath => Item.Path is not null;
    public bool HasAction => Item.Action is not null;
    public string ActionText => Item.Action?.Label ?? "";

    public string Glyph => Item switch
    {
        { Path: null } => "",              // history: restore points
        { Name: "Recycle Bin" } => "",     // delete
        { IsFolder: true } => "",          // folder
        _ => "",                           // page
    };
}

public sealed class TypeFilterViewModel
{
    public TypeFilterViewModel(string name, int count, long bytes)
    {
        Name = name;
        Count = count;
        Label = name == AllName ? $"All ({count})" : $"{name} ({count} · {Format.Bytes(bytes)})";
    }

    public const string AllName = "All";

    public string Name { get; }
    public int Count { get; }
    public string Label { get; }
}
