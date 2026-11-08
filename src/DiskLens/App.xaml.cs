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
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskLens");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:u}] {ex}\n\n");
            return path;
        }
        catch
        {
            return "(could not write crash log)";
        }
    }
}
