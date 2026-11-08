using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace DiskLens;

/// <summary>Small helpers for handing things off to Windows (Explorer, Settings, clipboard).</summary>
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

    public static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open {uri}.\n\n{ex.Message}", "DiskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static bool Start(string fileName, string arguments = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't start {Path.GetFileName(fileName)}.\n\n{ex.Message}", "DiskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
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
