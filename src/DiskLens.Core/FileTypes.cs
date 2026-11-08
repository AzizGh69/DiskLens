using System;
using System.Collections.Generic;

namespace DiskLens.Core;

/// <summary>Groups file extensions into the kinds of files people recognize.</summary>
public static class FileTypes
{
    public const string Other = "Other";

    private static readonly Dictionary<string, string> GroupByExtension = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Group names in display order.</summary>
    public static IReadOnlyList<string> Groups { get; } = new[]
    {
        "Videos", "Disk images & VMs", "Game data", "Archives", "Installers", "Music & audio",
        "Pictures", "Documents", "Apps & libraries", "System & logs", Other,
    };

    static FileTypes()
    {
        Add("Videos", "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "ts", "mts", "m2ts", "mpg", "mpeg", "3gp", "vob");
        Add("Disk images & VMs", "iso", "img", "vhd", "vhdx", "avhdx", "vmdk", "vdi", "wim", "esd", "qcow2", "dmg", "swm");
        Add("Game data", "pak", "ucas", "utoc", "vpk", "bundle", "assets", "resource", "forge", "big", "rpf", "pck", "bsa", "ba2", "gcf", "ff", "xpak", "sb", "cas", "toc", "unity3d", "uasset");
        Add("Archives", "zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "zst", "lz4", "cab");
        Add("Installers", "exe", "msi", "msix", "msixbundle", "appx", "appxbundle", "msp", "msu");
        Add("Music & audio", "mp3", "flac", "wav", "aac", "ogg", "m4a", "wma", "opus", "aiff", "alac");
        Add("Pictures", "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "raw", "cr2", "cr3", "nef", "arw", "dng", "heic", "webp", "psd", "svg");
        Add("Documents", "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "epub", "odt", "ods", "pst", "ost");
        Add("Apps & libraries", "dll", "so", "lib", "pdb", "jar", "node", "sys", "ocx", "bin", "dat", "onnx", "gguf", "safetensors", "ckpt", "pt", "pth", "h5", "tflite");
        Add("System & logs", "dmp", "etl", "edb", "evtx", "log", "blf", "regtrans-ms", "db", "sqlite", "sdb", "pf", "mdmp", "hdmp", "tmp", "cache");
    }

    private static void Add(string group, params string[] extensions)
    {
        foreach (var ext in extensions) GroupByExtension[ext] = group;
    }

    /// <summary>The group for a file name or bare extension (with or without the dot).</summary>
    public static string GroupOf(string extension)
    {
        string ext = extension.TrimStart('.');
        if (ext.Length == 0) return Other;
        return GroupByExtension.TryGetValue(ext, out var group) ? group : Other;
    }

    /// <summary>Segoe Fluent Icons glyph for a group.</summary>
    public static string GlyphOf(string group) => group switch
    {
        "Videos" => "",
        "Disk images & VMs" => "",
        "Game data" => "",
        "Archives" => "",
        "Installers" => "",
        "Music & audio" => "",
        "Pictures" => "",
        "Documents" => "",
        "Apps & libraries" => "",
        "System & logs" => "",
        _ => "",
    };
}
