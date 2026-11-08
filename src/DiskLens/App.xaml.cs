using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DiskLens.Theme;

namespace DiskLens;

public partial class App : Application
{
    private SnapshotOptions? _snapshot;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _snapshot = SnapshotOptions.Parse(e.Args);
        if (_snapshot?.Theme is string theme)
            ThemeMode = theme == "dark" ? ThemeMode.Dark : ThemeMode.Light;

        ChartColors.Apply(this, _snapshot?.Theme);
        if (_snapshot is null) ChartColors.FollowSystemTheme(this);

        var window = new MainWindow();
        if (_snapshot is not null)
        {
            window.Width = _snapshot.Width;
            window.Height = _snapshot.Height;
            window.ShowActivated = false;
        }

        MainWindow = window;
        window.Show();

        if (_snapshot is not null)
            _ = SnapshotRunner.RunAsync(window, _snapshot);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string log = WriteCrashLog(e.Exception);
        e.Handled = true;

        if (_snapshot is not null)
        {
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were saved to:\n{log}",
            "DiskLens", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    internal static string WriteCrashLog(Exception ex)
    {
        try
        {
            // This folder belongs to the user, but DiskLens runs as administrator: never follow a link someone
            // put there (it could point at a Windows file). Write a fresh file instead of appending.
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskLens");
            var folder = Directory.CreateDirectory(dir);
            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return "(crash log skipped: its folder is a link)";

            string path = Path.Combine(dir, "crash.log");
            if (File.Exists(path) || Directory.Exists(path)) File.Delete(path); // deletes a link itself, never its target
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write($"[{DateTime.Now:u}] {ex}\n");
            }

            return path;
        }
        catch
        {
            return "(could not write crash log)";
        }
    }
}
