using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using DiskLens.Core.Services;

namespace DiskLens;

/// <summary>
/// Small helpers for handing things off to Windows (Explorer, Settings, clipboard). Every program goes through
/// <see cref="TrustedPrograms"/> first: DiskLens runs as administrator, so it only starts programs from
/// Windows or Program Files, by full path.
/// </summary>
public static class Shell
{
    /// <summary>Opens Explorer with the file or folder selected.</summary>
    public static void ShowInExplorer(string path)
    {
        if (Directory.Exists(path) && Path.GetPathRoot(path) == path)
        {
            OpenFolder(path);
            return;
        }

        Start("explorer.exe", $"/select,\"{path}\"");
    }

    public static void OpenFolder(string path) => Start("explorer.exe", $"\"{path}\"");

    /// <summary>Opens a Settings page such as <c>ms-settings:appsfeatures</c>.</summary>
    public static void OpenUri(string uri) => Start(uri);

    public static bool Start(string fileName, string arguments = "")
    {
        string? resolved = TrustedPrograms.Resolve(fileName);
        if (resolved is null)
        {
            MessageBox.Show($"DiskLens didn't start \"{fileName}\": it only starts programs from Windows or Program Files.",
                "DiskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // Paths are passed in quotes. Windows doesn't allow quotes in file names, so an odd count means
        // something is wrong with the path: don't pass it on.
        if (CountQuotes(arguments) % 2 != 0) return false;

        try
        {
            Process.Start(new ProcessStartInfo(resolved, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.SystemDirectory,
            });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't start {Path.GetFileName(fileName)}.\n\n{ex.Message}", "DiskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static int CountQuotes(string s)
    {
        int n = 0;
        foreach (char c in s) if (c == '"') n++;
        return n;
    }

    /// <summary>Copies text to the clipboard, retrying briefly if another app holds it.</summary>
    public static bool CopyText(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60);
            }
        }

        return false;
    }
}
