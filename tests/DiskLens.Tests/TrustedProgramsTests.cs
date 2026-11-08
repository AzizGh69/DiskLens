using System;
using System.IO;
using DiskLens.Core.Services;
using Xunit;

namespace DiskLens.Tests;

public class TrustedProgramsTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    [Fact]
    public void Bare_names_resolve_to_System32_or_Windows_only()
    {
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"), TrustedPrograms.Resolve("cleanmgr.exe"), ignoreCase: true);
        Assert.Equal(Path.Combine(Windows, "explorer.exe"), TrustedPrograms.Resolve("explorer.exe"), ignoreCase: true);
        Assert.Null(TrustedPrograms.Resolve("not-a-real-program-1234.exe"));
    }

    [Theory]
    [InlineData(@"..\cleanmgr.exe")]
    [InlineData(@"tools\cleanmgr.exe")]
    [InlineData(@"C:\Users\Public\Downloads\RevoUnin.exe")]
    [InlineData(@"\\server\share\RevoUnin.exe")]
    [InlineData("http://example.com/setup.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:appsfeatures&calc")]
    [InlineData("")]
    public void Refuses_anything_outside_Windows_and_Program_Files(string fileName)
    {
        Assert.Null(TrustedPrograms.Resolve(fileName));
    }

    [Fact]
    public void Allows_Settings_pages()
    {
        Assert.Equal("ms-settings:appsfeatures", TrustedPrograms.Resolve("ms-settings:appsfeatures"));
        Assert.Equal("ms-settings:storagesense", TrustedPrograms.Resolve("ms-settings:storagesense"));
    }

    [Fact]
    public void Protected_folders_are_Windows_and_Program_Files()
    {
        Assert.True(TrustedPrograms.IsInProtectedFolder(Path.Combine(ProgramFiles, @"VS Revo Group\Revo Uninstaller\RevoUnin.exe")));
        Assert.True(TrustedPrograms.IsInProtectedFolder(Path.Combine(Windows, @"System32\cleanmgr.exe")));

        Assert.False(TrustedPrograms.IsInProtectedFolder(ProgramFiles + @" Evil\RevoUnin.exe"));
        Assert.False(TrustedPrograms.IsInProtectedFolder(Path.Combine(ProgramFiles, @"..\Users\Public\RevoUnin.exe")));
        Assert.False(TrustedPrograms.IsInProtectedFolder(@"RevoUnin.exe"));
        Assert.False(TrustedPrograms.IsInProtectedFolder(@"\\?\C:\Program Files\x.exe"));
    }
}
