using System.Collections.Generic;
using System.Linq;
using DiskLens.Core;
using DiskLens.Core.Models;
using DiskLens.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiskLens.Tests;

public class DiskInfoTests
{
    private readonly ITestOutputHelper _output;

    public DiskInfoTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}", null, null, PartitionKind.Boot, "EFI System")]
    [InlineData("{e3c9e316-0b5c-4db8-817d-f92df00215ae}", null, null, PartitionKind.Reserved, "Microsoft Reserved")]
    [InlineData("{de94bba4-06d1-4d40-a16a-bfd50179d6ac}", null, null, PartitionKind.Recovery, "Recovery")]
    [InlineData("{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", null, 'C', PartitionKind.WindowsDrive, "Basic data")]
    [InlineData("{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", null, 'D', PartitionKind.DataDrive, "Basic data")]
    [InlineData(null, 0x27, null, PartitionKind.Recovery, "Recovery")]
    [InlineData(null, 0x07, 'E', PartitionKind.DataDrive, "Basic data")]
    [InlineData(null, 0x83, null, PartitionKind.Other, "Linux")]
    [InlineData(null, 0x0F, null, PartitionKind.Other, "Extended")]
    public void Classify_recognizes_partition_types(string? gpt, int? mbr, char? letter, PartitionKind kind, string name)
    {
        var (k, n) = DiskInfoService.Classify(gpt, mbr, letter, 'C', isActive: false, isHidden: false);
        Assert.Equal(kind, k);
        Assert.Equal(name, n);
    }

    [Fact]
    public void Classify_marks_active_mbr_partition_without_letter_as_system_reserved()
    {
        var (k, n) = DiskInfoService.Classify(null, 0x07, null, 'C', isActive: true, isHidden: false);
        Assert.Equal(PartitionKind.Boot, k);
        Assert.Equal("System Reserved", n);
    }

    [Fact]
    public void AddWithGaps_inserts_unallocated_space_but_ignores_alignment_slack()
    {
        const long MB = 1024 * 1024;
        var disk = new PhysicalDiskInfo { Number = 0, Size = 1000 * MB };
        var parts = new List<PartitionInfo>
        {
            new() { Offset = 1 * MB, Size = 100 * MB },   // 1 MB alignment gap: ignored
            new() { Offset = 101 * MB, Size = 500 * MB },
        };

        DiskInfoService.AddWithGaps(disk, parts);

        Assert.Equal(3, disk.Partitions.Count);
        var gap = disk.Partitions[2];
        Assert.Equal(PartitionKind.Unallocated, gap.Kind);
        Assert.Equal(399 * MB, gap.Size);
    }

    [Fact]
    public void Load_reads_this_pcs_drives_and_disks()
    {
        var snapshot = DiskInfoService.Load();

        Assert.NotEmpty(snapshot.Drives);
        Assert.Contains(snapshot.Drives, d => d.IsWindowsDrive);

        foreach (var drive in snapshot.Drives)
            _output.WriteLine($"{drive.DisplayName} {drive.FileSystem} {Format.Bytes(drive.TotalSize)} free {Format.Bytes(drive.FreeSpace)} disk {drive.DiskNumber}");

        if (snapshot.PartitionError is not null) _output.WriteLine(snapshot.PartitionError);
        foreach (var disk in snapshot.Disks)
        {
            _output.WriteLine($"Disk {disk.Number} {disk.FriendlyName} {disk.BusType} {disk.PartitionStyle} {Format.Bytes(disk.Size)}");
            foreach (var p in disk.Partitions)
                _output.WriteLine($"   #{p.PartitionNumber} {p.DisplayName} [{p.Kind}] {Format.Bytes(p.Size)} fs={p.FileSystem} used={(p.UsedSpace is long u ? Format.Bytes(u) : "-")}");
        }

        Assert.True(snapshot.PartitionError is not null || snapshot.Disks.Any(d => d.Partitions.Count > 0));
    }
}
