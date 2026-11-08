using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DiskLens.Controls;

/// <summary>
/// Lays children out left-to-right with widths proportional to their <see cref="WeightProperty"/>,
/// separated by a small gap. Tiny items get <see cref="MinItemWidth"/> so they stay visible.
/// </summary>
public sealed class WeightPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(WeightPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetWeight(UIElement element) => (double)element.GetValue(WeightProperty);
    public static void SetWeight(UIElement element, double value) => element.SetValue(WeightProperty, value);

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(WeightPanel),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(WeightPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double total = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var (children, widths) = ComputeWidths(total);
        double height = 0;
        for (int i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(widths[i], availableSize.Height));
            height = Math.Max(height, children[i].DesiredSize.Height);
        }

        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Measure(new Size(0, 0));

        return new Size(total, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (children, widths) = ComputeWidths(finalSize.Width);
        double x = 0;
        for (int i = 0; i < children.Count; i++)
        {
            children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i] + Spacing;
        }

        return finalSize;
    }

    private (List<UIElement> Children, double[] Widths) ComputeWidths(double total)
    {
        var children = new List<UIElement>();
        foreach (UIElement child in InternalChildren)
            if (child.Visibility != Visibility.Collapsed) children.Add(child);

        int n = children.Count;
        var widths = new double[n];
        if (n == 0) return (children, widths);

        double usable = Math.Max(0, total - Spacing * (n - 1));
        double min = MinItemWidth;

        if (min * n >= usable)
        {
            for (int i = 0; i < n; i++) widths[i] = usable / n;
            return (children, widths);
        }

        var weights = new double[n];
        for (int i = 0; i < n; i++) weights[i] = Math.Max(0, GetWeight(children[i]));

        // Give the minimum width to items that would be too thin, then share the rest by weight.
        var pinned = new bool[n];
        bool changed = true;
        while (changed)
        {
            changed = false;
            double remainingWidth = usable, remainingWeight = 0;
            for (int i = 0; i < n; i++)
            {
                if (pinned[i]) remainingWidth -= min;
                else remainingWeight += weights[i];
            }

            for (int i = 0; i < n; i++)
            {
                if (pinned[i]) { widths[i] = min; continue; }
                widths[i] = remainingWeight > 0 ? weights[i] / remainingWeight * remainingWidth : remainingWidth / n;
                if (widths[i] < min) { pinned[i] = true; changed = true; }
            }
        }

        return (children, widths);
    }
}
