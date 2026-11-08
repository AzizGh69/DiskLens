using System;
using System.IO;
using System.Linq;
using DiskLens.Core.Models;
using DiskLens.Core.Services;
using Xunit;

namespace DiskLens.Tests;

public class RecycleGuardTests
{
    private static readonly (string, string)[] Apps =
    {
        (@"C:\Program Files\Big App", "Big App"),
        (@"D:\SteamLibrary\steamapps\common\Game", "Game"),
    };

    private static RecycleGuard Guard(string root = @"C:\", bool hasBin = true) =>
        new(root, hasBin, Apps, windowsDir: @"C:\Windows");

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Windows.old")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21")]
    [InlineData(@"C:\System Volume Information")]
    [InlineData(@"C:\Recovery")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\hiberfil.sys")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)\Some Tool\tool.exe")]
    [InlineData(@"C:\Program Files\Big App\data\cache.bin")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows")]
    [InlineData(@"C:\ProgramData\Package Cache\{abc}")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"C:\Users\me\Documents")]
    [InlineData(@"C:\Users\me\OneDrive - Contoso")]
    [InlineData(@"C:\Users\me\AppData")]
    [InlineData(@"C:\Users\me\AppData\Local")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu")]
    [InlineData(@"C:\Users\me\AppData\Local\Programs")]
    [InlineData(@"C:\Users\Default\NTUSER.DAT")]
    public void Blocks_what_Windows_and_apps_need(string path)
    {
        Assert.NotNull(Guard().BlockReason(path));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\old-installer.exe")]
    [InlineData(@"C:\Users\me\Documents\Videos\holiday.mp4")]
    [InlineData(@"C:\Users\me\AppData\Local\SomeApp\cache")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\big.tmp")]
    [InlineData(@"C:\ProgramData\OldVendor")]
    [InlineData(@"C:\Backups\2019")]
    [InlineData(@"C:\Program Files Backup.zip")]
    public void Allows_personal_files_and_leftovers(string path)
    {
        Assert.Null(Guard().BlockReason(path));
    }

    [Fact]
    public void Points_to_Revo_for_installed_apps_and_folders_that_contain_them()
    {
        var guard = Guard(@"D:\");
        Assert.Contains("Revo", guard.BlockReason(@"D:\SteamLibrary\steamapps\common\Game\data.pak"));
        Assert.Contains("Game", guard.BlockReason(@"D:\SteamLibrary")!);
        Assert.Null(guard.BlockReason(@"D:\SteamLibrary\steamapps\downloading"));
    }

    [Fact]
    public void Refuses_drives_without_a_Recycle_Bin()
    {
        Assert.Contains("permanent", Guard(@"E:\", hasBin: false).BlockReason(@"E:\photos\a.jpg"));
    }

    [Fact]
    public void Refuses_items_on_another_drive()
    {
        Assert.NotNull(Guard(@"C:\").BlockReason(@"D:\file.txt"));
    }
}

public class ApplyRecycledTests
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void A_recycled_folder_moves_its_size_to_the_Recycle_Bin()
    {
        var tree = new TestTree()
            .Folder(@"Users\me\Downloads\Old", 300 * MB)
            .Folder(@"Users\me\Downloads", 100 * MB);
        var scan = tree.Build();
        scan.LargestFiles.Add(new FileEntry(@"C:\Users\me\Downloads\Old\big.iso", 300 * MB, DateTime.UtcNow));
        long total = scan.TotalBytes;

        scan.ApplyRecycled(@"C:\Users\me\Downloads\Old", isFolder: true, 0);

        Assert.Null(scan.FindFolder(@"C:\Users\me\Downloads\Old"));
        Assert.Equal(100 * MB, scan.FindFolder(@"C:\Users\me\Downloads")!.Size);
        Assert.Equal(300 * MB, scan.FindFolder(@"C:\$Recycle.Bin")!.Size);
        Assert.Equal(total, scan.TotalBytes); // still on the drive until the bin is emptied
        Assert.Empty(scan.LargestFiles);
    }

    [Fact]
    public void A_recycled_file_is_subtracted_from_its_folders()
    {
        var scan = new TestTree().Folder(@"Data", 500 * MB).Folder(@"$Recycle.Bin\S-1", 10 * MB).Build();
        scan.LargestFiles.Add(new FileEntry(@"C:\Data\movie.mkv", 400 * MB, DateTime.UtcNow));

        scan.ApplyRecycled(@"C:\Data\movie.mkv", isFolder: false, 400 * MB);

        Assert.Equal(100 * MB, scan.FindFolder(@"C:\Data")!.Size);
        Assert.Equal(410 * MB, scan.FindFolder(@"C:\$Recycle.Bin")!.Size);
        Assert.Empty(scan.LargestFiles);
    }
}

/// <summary>Really moves a temporary file to the Recycle Bin. Opt-in: set DISKLENS_RECYCLE_TEST=1.</summary>
public class RecycleBinTests
{
    [Fact]
    public void Moves_a_temp_file_to_the_Recycle_Bin()
    {
        if (Environment.GetEnvironmentVariable("DISKLENS_RECYCLE_TEST") != "1") return;

        string folder = Path.Combine(Path.GetTempPath(), "DiskLens-recycle-test");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"DiskLens-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "DiskLens Recycle Bin test file — safe to delete.");

        var outcome = RecycleBin.Move(file, IntPtr.Zero);

        Assert.Equal(RecycleOutcome.Moved, outcome);
        Assert.False(File.Exists(file));
    }
}
