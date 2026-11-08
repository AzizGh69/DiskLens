using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DiskLens.Core;
using DiskLens.Core.Models;
using DiskLens.Core.Services;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

/// <summary>The Folders tab: drill down through the scanned tree, biggest first.</summary>
public sealed class FolderBrowserViewModel : ObservableObject
{
    private const int MaxFilesShown = 200;

    private readonly Func<IPathItem, string?> _blockReason;
    private FolderNode? _current;
    private string _summary = "";
    private int _version;

    public FolderBrowserViewModel(Func<IPathItem, string?> blockReason)
    {
        _blockReason = blockReason;
        UpCommand = RelayCommand.From(() => { if (_current?.Parent is { } p) NavigateTo(p); }, () => _current?.Parent is not null);
        NavigateCommand = new RelayCommand(p => { if (p is FolderNode n) NavigateTo(n); });
    }

    public FolderNode? Current => _current;
    public ObservableCollection<FolderNode> Breadcrumbs { get; } = new();
    public ObservableCollection<FolderItemViewModel> Items { get; } = new();
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public ICommand UpCommand { get; }
    public ICommand NavigateCommand { get; }

    public void Reset(FolderNode root) => NavigateTo(root);

    public async void NavigateTo(FolderNode node)
    {
        int version = ++_version;
        _current = node;
        OnPropertyChanged(nameof(Current));

        Breadcrumbs.Clear();
        var chain = new List<FolderNode>();
        for (var n = node; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        foreach (var n in chain) Breadcrumbs.Add(n);

        var items = node.Children.Select(c => new FolderItemViewModel(c, node.Size)).ToList();
        SetItems(items);
        Summary = Describe(node, filesLoading: node.OwnFileCount > 0);

        if (node.OwnFileCount == 0) return;

        string path = node.FullPath;
        var files = await Task.Run(() => Scanner.ListFiles(path, MaxFilesShown));
        if (version != _version) return;   // the user moved on

        items.AddRange(files.Select(f => new FolderItemViewModel(f, node.Size)));
        SetItems(items.OrderByDescending(i => i.Size).ToList());
        Summary = Describe(node, filesLoading: false);
    }

    /// <summary>Re-reads the current folder after something changed (e.g. an item was recycled).</summary>
    public void Refresh()
    {
        if (_current is not null) NavigateTo(_current);
    }

    private void SetItems(List<FolderItemViewModel> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            item.BlockReason = _blockReason(item);
            Items.Add(item);
        }
    }

    private static string Describe(FolderNode node, bool filesLoading)
    {
        string text = $"{Format.Bytes(node.Size)} in {Format.Count(node.FileCount)} files · {node.Children.Count} subfolders";
        if (node.OwnFileCount > 0)
            text += $" · {Format.Count(node.OwnFileCount)} files directly here ({Format.Bytes(node.OwnFilesSize)})";
        if (node.OwnFileCount > MaxFilesShown && !filesLoading)
            text += $", largest {MaxFilesShown} shown";
        if (node.Unreadable) text += " · Windows blocked part of this folder";
        return text;
    }
}
