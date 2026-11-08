using System.Linq;
using DiskLens.Core.Services;
using Xunit;

namespace DiskLens.Tests;

public class CategorizerTests
{
    private static TestTree SampleDrive() => new TestTree()
        .Folder(@"Windows\System32", 100)
        .Folder(@"Program Files\Big App", 50)
        .Folder(@"Users\me\AppData\Local\Cache", 30)
        .Folder(@"Users\me\Documents", 20)
        .Folder(@"Users\me\Downloads", 25)
        .Folder(@"Users\Default", 5)
        .Folder(@"Games\Game1", 40)
        .Folder(@"Stuff", 7)
        .RootFile("pagefile.sys", 16)
        .RootFile("notes.txt", 1);

    [Fact]
    public void Splits_space_into_categories_that_add_up_to_the_drive()
    {
        var tree = SampleDrive();
        var scan = tree.Build();

        var b = Categorizer.Compute(scan, driveUsed: 400, driveFree: 600, appFolders: new[] { tree.Node(@"Games\Game1") });

        Assert.Equal(100 + 5 + 16, b.System);      // Windows, default profile, pagefile
        Assert.Equal(50 + 30 + 40, b.Apps);        // Program Files, AppData, installed game
        Assert.Equal(20 + 25, b.User);             // Documents, Downloads
        Assert.Equal(7 + 1, b.Other);              // Stuff, notes.txt
        Assert.Equal(400 - scan.TotalBytes, b.Unreadable);
        Assert.Equal(1000, b.Total);
    }

    [Fact]
    public void Unknown_folders_are_other_when_no_app_lives_there()
    {
        var scan = SampleDrive().Build();
        var b = Categorizer.Compute(scan, driveUsed: scan.TotalBytes, driveFree: 0);

        Assert.Equal(40 + 7 + 1, b.Other);
        Assert.Equal(0, b.Unreadable);
    }

    [Fact]
    public void Heaviest_folders_look_inside_grouping_folders_and_skip_windows()
    {
        var scan = SampleDrive().Build();

        var top = Categorizer.HeaviestFolders(scan, 4).Select(f => f.FullPath).ToList();

        Assert.Equal(new[] { @"C:\Program Files\Big App", @"C:\Games", @"C:\Users\me\AppData\Local\Cache", @"C:\Users\me\Downloads" }, top);
        Assert.DoesNotContain(@"C:\Windows", top);
    }
}
