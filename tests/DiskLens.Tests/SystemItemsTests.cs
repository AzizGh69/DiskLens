using System.Linq;
using DiskLens.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiskLens.Tests;

public class SystemItemsTests
{
    private const long MB = 1024 * 1024;
    private const long GB = 1024 * MB;
    private readonly ITestOutputHelper _output;

    public SystemItemsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Find_lists_known_items_biggest_first()
    {
        var scan = new TestTree()
            .RootFile("pagefile.sys", 16 * GB)
            .RootFile("hiberfil.sys", 6 * GB)
            .Folder(@"Windows\WinSxS", 9 * GB)
            .Folder(@"Windows\SoftwareDistribution\Download", 2 * GB)
            .Folder(@"$Recycle.Bin\S-1-5-21", 3 * GB)
            .Folder(@"Users\me\AppData\Local\Temp", 1 * GB)
            .Folder(@"Users\other\AppData\Local\Temp", 512 * MB)
            .Folder(@"Users\me\Documents", 50 * GB)
            .Build();

        var items = SystemItemsService.Find(scan, restorePointBytes: 4 * GB);
        foreach (var i in items) _output.WriteLine($"{i.Name} | {i.Size / MB} MB | {i.Path ?? "-"} | {i.Action?.Label}");

        var names = items.Select(i => i.Name).ToList();
        Assert.Equal("Page file (virtual memory)", names[0]);
        Assert.Equal("Windows component store (WinSxS)", names[1]);
        Assert.Contains("Hibernation file", names);
        Assert.Contains("System Restore points", names);
        Assert.Contains("Recycle Bin", names);
        Assert.Contains("Windows Update downloads", names);
        Assert.DoesNotContain(items, i => i.Name.Contains("Documents"));

        Assert.True(items.Zip(items.Skip(1)).All(p => p.First.Size >= p.Second.Size), "sorted by size");

        var temps = items.Single(i => i.Name == "Apps' temporary files");
        Assert.Equal(1 * GB + 512 * MB, temps.Size);
        Assert.Equal(@"C:\Users\me\AppData\Local\Temp", temps.Path);

        var restore = items.Single(i => i.Name == "System Restore points");
        Assert.Null(restore.Path);
        Assert.Equal("SystemPropertiesProtection.exe", restore.Action?.Target);

        Assert.NotNull(items.Single(i => i.Name.StartsWith("Windows component store")).SizeNote);
        Assert.Equal("/d C", items.Single(i => i.Name == "Windows Update downloads").Action?.Arguments);
    }

    [Fact]
    public void Find_skips_tiny_and_unknown_restore_points()
    {
        var scan = new TestTree()
            .RootFile("swapfile.sys", 256 * 1024) // under 1 MB
            .Folder(@"Windows\Temp", 10)
            .Build();

        var items = SystemItemsService.Find(scan, restorePointBytes: null);

        Assert.DoesNotContain(items, i => i.Name == "Swap file");
        Assert.DoesNotContain(items, i => i.Name == "Windows temporary files");
        Assert.DoesNotContain(items, i => i.Name == "System Restore points");
    }

    [Theory]
    [InlineData(@"\\?\Volume{1b2c3d4e-0000-0000-0000-100000000000}\", "Volume{1b2c3d4e-0000-0000-0000-100000000000}")]
    [InlineData(@"Win32_Volume.DeviceID=""\\\\?\\Volume{1b2c3d4e-0000-0000-0000-100000000000}\\""", "Volume{1b2c3d4e-0000-0000-0000-100000000000}")]
    [InlineData("C:", null)]
    [InlineData(null, null)]
    public void VolumeGuidOf_extracts_the_volume_id(string? text, string? expected)
    {
        Assert.Equal(expected, SystemItemsService.VolumeGuidOf(text));
    }
}
