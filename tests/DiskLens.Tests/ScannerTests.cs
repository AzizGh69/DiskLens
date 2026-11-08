using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DiskLens.Core.Services;
using Xunit;

namespace DiskLens.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DiskLensTests", Guid.NewGuid().ToString("N"));

    public ScannerTests()
    {
        Directory.CreateDirectory(_root);
        MakeFile("a.bin", 1_000);
        MakeFile(@"big\video.mp4", 5_000_000);
        MakeFile(@"big\sub\x.dat", 2_000_000);
        MakeFile(@"small\s.txt", 10);
        var hidden = MakeFile(@"small\hidden.dat", 300);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Adds_up_sizes_including_hidden_and_system_files()
    {
        var result = new Scanner().Scan(_root);

        Assert.Equal(1_000 + 5_000_000 + 2_000_000 + 10 + 300, result.TotalBytes);
        Assert.Equal(5, result.FileCount);
        Assert.Equal(7_000_000, result.FindFolder(Path.Combine(_root, "big"))!.Size);
        Assert.Equal(310, result.FindFolder(Path.Combine(_root, "small"))!.Size);
        Assert.Equal(0, result.FindFolder(Path.Combine(_root, "empty"))!.Size);
    }

    [Fact]
    public void Sorts_children_biggest_first_and_lists_largest_files()
    {
        var result = new Scanner().Scan(_root);

        Assert.Equal("big", result.Root.Children[0].Name);
        Assert.Equal("video.mp4", result.LargestFiles[0].Name);
        Assert.Equal("x.dat", result.LargestFiles[1].Name);
        Assert.True(result.LargestFiles.Zip(result.LargestFiles.Skip(1)).All(p => p.First.Size >= p.Second.Size));
    }

    [Fact]
    public void Keeps_only_the_requested_number_of_largest_files()
    {
        var result = new Scanner { TopFileCount = 2 }.Scan(_root);
        Assert.Equal(new[] { "video.mp4", "x.dat" }, result.LargestFiles.Select(f => f.Name));
    }

    [Fact]
    public void Does_not_follow_junctions_so_nothing_is_counted_twice()
    {
        string link = Path.Combine(_root, "link-to-big");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{Path.Combine(_root, "big")}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        mklink.WaitForExit();
        Assert.True(Directory.Exists(link), "test setup: junction was not created");

        var result = new Scanner().Scan(_root);

        Assert.Null(result.Root.FindChild("link-to-big"));
        Assert.Equal(7_001_310, result.TotalBytes);
    }

    [Fact]
    public void Records_root_files_for_categorizing()
    {
        var result = new Scanner().Scan(_root);
        Assert.Equal(new[] { "a.bin" }, result.RootFiles.Select(f => f.Name));
    }

    [Fact]
    public void Can_be_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Scanner().Scan(_root, cts.Token));
    }

    private string MakeFile(string relative, long size)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(size);
        return path;
    }
}
