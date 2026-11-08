using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiskLens.ViewModels;

namespace DiskLens;

/// <summary>
/// Developer option: <c>DiskLens.exe --snapshot out.png [--page D] [--scan] [--tab 1] [--theme dark]</c>
/// renders the window to a PNG and exits. Used to check layouts and make README screenshots.
/// </summary>
public sealed class SnapshotOptions
{
    public string Path { get; private init; } = "";
    public string? Page { get; private init; }
    public bool Scan { get; private init; }
    public int? Tab { get; private init; }
    public string? Theme { get; private init; }
    public double Width { get; private init; } = 1320;
    public double Height { get; private init; } = 860;
    public string? Folder { get; private init; }

    public static SnapshotOptions? Parse(string[] args)
    {
        int i = Array.IndexOf(args, "--snapshot");
        if (i < 0 || i + 1 >= args.Length) return null;

        string? Value(string name)
        {
            int k = Array.IndexOf(args, name);
            return k >= 0 && k + 1 < args.Length ? args[k + 1] : null;
        }

        return new SnapshotOptions
        {
            Path = System.IO.Path.GetFullPath(args[i + 1]),
            Page = Value("--page"),
            Scan = args.Contains("--scan"),
            Tab = int.TryParse(Value("--tab"), out int t) ? t : null,
            Theme = Value("--theme")?.ToLowerInvariant(),
            Width = double.TryParse(Value("--width"), out double w) ? w : 1320,
            Height = double.TryParse(Value("--height"), out double h) ? h : 860,
            Folder = Value("--folder"),
        };
    }
}

public static class SnapshotRunner
{
    public static async Task RunAsync(MainWindow window, SnapshotOptions options)
    {
        try
        {
            await window.Ready;
            var vm = window.ViewModel;

            if (options.Page is string page && !page.Equals("overview", StringComparison.OrdinalIgnoreCase))
            {
                var drive = vm.Pages.OfType<DriveViewModel>().FirstOrDefault(d => char.ToUpperInvariant(page[0]) == d.Drive.Letter);
                if (drive is not null)
                {
                    vm.SelectedPage = drive;
                    if (options.Scan) await drive.ScanAsync();
                    drive.ApplySnapshotOptions(options);
                }
            }

            await Task.Delay(800);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Save(window, options.Path);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            App.WriteCrashLog(ex);
            Application.Current.Shutdown(1);
        }
    }

    private static void Save(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        double scale = VisualTreeHelper.GetDpi(root).PixelsPerDip;
        double w = root.ActualWidth, h = root.ActualHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = Application.Current.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            var brush = new VisualBrush(root)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
