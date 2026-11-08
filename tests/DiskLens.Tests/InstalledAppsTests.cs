using System;
using System.Linq;
using DiskLens.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiskLens.Tests;

public class InstalledAppsTests
{
    private const long MB = 1024 * 1024;
    private readonly ITestOutputHelper _output;

    public InstalledAppsTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("\"C:\\Program Files\\Foo\\uninst.exe\" /S", @"C:\Program Files\Foo")]
    [InlineData(@"C:\Program Files\Foo\app.exe,0", @"C:\Program Files\Foo")]
    [InlineData(@"C:\Program Files\My App\unins000.exe", @"C:\Program Files\My App")]
    [InlineData(@"C:\Program Files\My App\unins000.exe /SILENT", @"C:\Program Files\My App")]
    [InlineData("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", null)]
    [InlineData("", null)]
    [InlineData("\"unterminated", null)]
    public void FolderOfCommand_finds_the_program_folder(string command, string? expected)
    {
        Assert.Equal(expected, InstalledAppsService.FolderOfCommand(command));
    }

    [Theory]
    [InlineData("\"D:\\Games\\Foo\\\"", @"D:\Games\Foo")]
    [InlineData(@"D:\Games\Foo\", @"D:\Games\Foo")]
    [InlineData("   ", null)]
    [InlineData("relative\\path", null)]
    public void NormalizeLocation_trims_quotes_and_slashes(string raw, string? expected)
    {
        Assert.Equal(expected, InstalledAppsService.NormalizeLocation(raw));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Foo", true)]
    [InlineData(@"D:\Games\Foo", true)]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files (x86)", false)]
    [InlineData(@"C:\ProgramData", false)]
    [InlineData(@"C:\Program Files\Common Files", false)]
    [InlineData(@"C:\Program Files (x86)\Common Files\Foo", false)]
    [InlineData(@"C:\ProgramData\Package Cache\{abc}", false)]
    public void IsUsableLocation_rejects_shared_folders(string folder, bool expected)
    {
        Assert.Equal(expected, InstalledAppsService.IsUsableLocation(folder));
    }

    [Theory]
    [InlineData(@"D:\Android\Android Studio\bin", @"D:\Android\Android Studio")]
    [InlineData(@"C:\Program Files\App\Uninstall", @"C:\Program Files\App")]
    [InlineData(@"C:\Program Files\App", @"C:\Program Files\App")]
    [InlineData(@"C:\Program Files\bin", @"C:\Program Files\bin")]
    public void GuessAppFolder_steps_out_of_helper_folders(string folder, string expected)
    {
        Assert.Equal(expected, InstalledAppsService.GuessAppFolder(folder));
    }

    [Fact]
    public void IsUsableLocation_rejects_the_Windows_folder()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.False(InstalledAppsService.IsUsableLocation(windows + @"\System32"));
    }

    private static InstalledApp App(string name, string? location, long estimateMb = 0, bool reliable = true) => new()
    {
        Name = name,
        InstallLocation = location,
        LocationIsReliable = reliable,
        EstimatedSize = estimateMb * MB,
    };

    [Fact]
    public void Measure_uses_the_scanned_folder_size()
    {
        var scan = new TestTree()
            .Folder(@"Program Files\Big\bin", 900 * MB)
            .Folder(@"Program Files\Small", 10 * MB)
            .Build();

        var sizes = InstalledAppsService.Measure(scan, new[] { App("Small", @"C:\Program Files\Small", 1), App("Big", @"C:\Program Files\Big", 5) }, isWindowsDrive: true);

        Assert.Equal(new[] { "Big", "Small" }, sizes.Select(s => s.App.Name));
        Assert.Equal(900 * MB, sizes[0].Size);
        Assert.False(sizes[0].IsEstimate);
        Assert.NotNull(sizes[0].Folder);
    }

    [Fact]
    public void Measure_subtracts_apps_nested_in_another_apps_folder()
    {
        var scan = new TestTree()
            .Folder(@"Program Files (x86)\Launcher", 100 * MB)
            .Folder(@"Program Files (x86)\Launcher\games\Game", 5000 * MB)
            .Build();

        var sizes = InstalledAppsService.Measure(scan, new[]
        {
            App("Launcher", @"C:\Program Files (x86)\Launcher"),
            App("Game", @"C:\Program Files (x86)\Launcher\games\Game"),
        }, isWindowsDrive: true);

        Assert.Equal(5000 * MB, sizes.Single(s => s.App.Name == "Game").Size);
        Assert.Equal(100 * MB, sizes.Single(s => s.App.Name == "Launcher").Size);
    }

    [Fact]
    public void Measure_counts_a_shared_folder_once()
    {
        var scan = new TestTree().Folder(@"Program Files\Office", 3000 * MB).Build();

        var sizes = InstalledAppsService.Measure(scan, new[]
        {
            App("Office language pack", @"C:\Program Files\Office", 50, reliable: false),
            App("Office", @"C:\Program Files\Office", 2000),
        }, isWindowsDrive: true);

        var office = sizes.Single(s => s.App.Name == "Office");
        Assert.Equal(3000 * MB, office.Size);
        Assert.False(office.IsEstimate);

        var pack = sizes.Single(s => s.App.Name == "Office language pack");
        Assert.True(pack.IsEstimate);
        Assert.Equal(50 * MB, pack.Size);
    }

    [Fact]
    public void Measure_falls_back_to_the_estimate_and_filters_by_drive()
    {
        var scan = new TestTree().Folder(@"Program Files\Here", 10 * MB).Build();

        var apps = new[]
        {
            App("Here", @"C:\Program Files\Here"),
            App("Missing folder", @"C:\Program Files\Gone", 300),
            App("No folder", null, 40),
            App("No folder, no estimate", null),
            App("Other drive", @"D:\Games\Thing", 999),
        };

        var onWindowsDrive = InstalledAppsService.Measure(scan, apps, isWindowsDrive: true);
        Assert.Equal(new[] { "Missing folder", "No folder", "Here" }, onWindowsDrive.Select(s => s.App.Name));
        Assert.True(onWindowsDrive[0].IsEstimate);

        var onDataDrive = InstalledAppsService.Measure(scan, apps, isWindowsDrive: false);
        Assert.Equal(new[] { "Missing folder", "Here" }, onDataDrive.Select(s => s.App.Name));
    }

    [Fact]
    public void Measure_prefers_the_estimate_when_a_guessed_folder_looks_too_small()
    {
        // Location guessed from the uninstaller (e.g. ...\App\uninstall\unins.exe) only covers a subfolder.
        var scan = new TestTree().Folder(@"Program Files\App\uninstall", 1 * MB).Build();

        var sizes = InstalledAppsService.Measure(scan, new[] { App("App", @"C:\Program Files\App\uninstall", 800, reliable: false) }, isWindowsDrive: true);

        Assert.True(sizes[0].IsEstimate);
        Assert.Equal(800 * MB, sizes[0].Size);
    }

    [Fact]
    public void Load_reads_this_PCs_installed_apps()
    {
        var apps = InstalledAppsService.Load();
        _output.WriteLine($"{apps.Count} apps");
        foreach (var a in apps.Take(15))
            _output.WriteLine($"{a.Name} | {a.InstallLocation ?? "-"} | reliable={a.LocationIsReliable} | {a.EstimatedSize / MB} MB");

        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));

        var revo = RevoLocator.Find(apps);
        _output.WriteLine(revo is null ? "Revo: not found" : $"Revo: {revo.ExePath} (Pro={revo.IsPro}, {revo.Version})");
    }
}
