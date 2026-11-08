using System;
using System.IO;
using System.Linq;
using DiskLens.Core;
using DiskLens.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiskLens.Tests;

/// <summary>
/// Scans a real drive and prints what DiskLens would show. Only runs when DISKLENS_SCAN_DRIVE is set,
/// e.g. <c>$env:DISKLENS_SCAN_DRIVE = "C"; dotnet test --filter RealDrive</c>
/// (or DISKLENS_SCAN_PATH for a speed comparison on one folder).
/// </summary>
public class RealDriveTests
{
    private readonly ITestOutputHelper _output;

    public RealDriveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RealDrive_folder_timing()
    {
        string? path = Environment.GetEnvironmentVariable("DISKLENS_SCAN_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;

        foreach (var (workers, buffer) in new[] { (8, 16 * 1024), (16, 32 * 1024), (32, 64 * 1024) })
        {
            var result = new Scanner { WorkerCount = workers, BufferSize = buffer }.Scan(path);
            _output.WriteLine($"workers={workers} buffer={buffer / 1024}K: {result.Duration.TotalSeconds:0.0} s, {Format.Count(result.FileCount)} files");
        }
    }

    [Fact]
    public void RealDrive_scan_report()
    {
        string? letter = Environment.GetEnvironmentVariable("DISKLENS_SCAN_DRIVE");
        if (string.IsNullOrWhiteSpace(letter)) return;

        var drive = new DriveInfo(letter);
        var result = new Scanner().Scan(drive.RootDirectory.FullName);
        long used = drive.TotalSize - drive.TotalFreeSpace;
        var b = Categorizer.Compute(result, used, drive.TotalFreeSpace);

        _output.WriteLine($"Scanned {drive.Name} in {Format.Duration(result.Duration)}: {Format.Count(result.FileCount)} files, {Format.Count(result.FolderCount)} folders, {Format.Bytes(result.TotalBytes)} of {Format.Bytes(used)} used, {result.UnreadableCount} unreadable");
        _output.WriteLine($"System {Format.Bytes(b.System)} | Apps {Format.Bytes(b.Apps)} | User {Format.Bytes(b.User)} | Other {Format.Bytes(b.Other)} | Unreadable {Format.Bytes(b.Unreadable)} | Free {Format.Bytes(b.Free)}");
        _output.WriteLine("Top-level: " + string.Join(", ", result.Root.Children.Take(8).Select(c => $"{c.Name} {Format.Bytes(c.Size)}")));
        _output.WriteLine("Root files: " + string.Join(", ", result.RootFiles.OrderByDescending(f => f.Size).Take(5).Select(f => $"{f.Name} {Format.Bytes(f.Size)}")));
        _output.WriteLine($"Largest file size: {Format.Bytes(result.LargestFiles.FirstOrDefault()?.Size ?? 0)}; #500: {Format.Bytes(result.LargestFiles.LastOrDefault()?.Size ?? 0)}");
        _output.WriteLine("Heaviest folder sizes: " + string.Join(", ", Categorizer.HeaviestFolders(result, 3).Select(f => Format.Bytes(f.Size))));

        Assert.True(result.TotalBytes > 0);
    }
}
