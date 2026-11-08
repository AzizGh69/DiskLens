using System;
using System.Windows;
using System.Windows.Media;

namespace DiskLens.Controls;

/// <summary>
/// A used / free capacity bar: solid used part, a 2px gap, then the light free part, with rounded ends.
/// </summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty UsedProperty = DependencyProperty.Register(
        nameof(Used), typeof(double), typeof(UsageBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TotalProperty = DependencyProperty.Register(
        nameof(Total), typeof(double), typeof(UsageBar), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UsedBrushProperty = DependencyProperty.Register(
        nameof(UsedBrush), typeof(Brush), typeof(UsageBar), new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FreeBrushProperty = DependencyProperty.Register(
        nameof(FreeBrush), typeof(Brush), typeof(UsageBar), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public UsageBar()
    {
        SnapsToDevicePixels = true;
    }

    public double Used { get => (double)GetValue(UsedProperty); set => SetValue(UsedProperty, value); }
    public double Total { get => (double)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }
    public Brush UsedBrush { get => (Brush)GetValue(UsedBrushProperty); set => SetValue(UsedBrushProperty, value); }
    public Brush FreeBrush { get => (Brush)GetValue(FreeBrushProperty); set => SetValue(FreeBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double gap = 2;
        double radius = Math.Min(h / 2, 4);
        double share = Total > 0 ? Math.Clamp(Used / Total, 0, 1) : 0;
        double usedWidth = w * share;

        if (share >= 0.999)
        {
            dc.DrawRoundedRectangle(UsedBrush, null, new Rect(0, 0, w, h), radius, radius);
            return;
        }

        if (usedWidth > 0.5)
            dc.DrawRoundedRectangle(UsedBrush, null, new Rect(0, 0, Math.Max(usedWidth, h / 2), h), radius, radius);

        double freeStart = usedWidth > 0.5 ? Math.Max(usedWidth, h / 2) + gap : 0;
        if (w - freeStart > 0.5)
            dc.DrawRoundedRectangle(FreeBrush, null, new Rect(freeStart, 0, w - freeStart, h), radius, radius);
    }
}
