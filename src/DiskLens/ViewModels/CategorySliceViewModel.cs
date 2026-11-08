using System.Windows;
using System.Windows.Media;
using DiskLens.Core;

namespace DiskLens.ViewModels;

/// <summary>One segment of the "Where your space goes" bar, plus its legend row.</summary>
public sealed class CategorySliceViewModel
{
    public CategorySliceViewModel(string name, string description, long bytes, long total, string brushKey)
    {
        Name = name;
        Description = description;
        Bytes = bytes;
        Percent = Format.Share(bytes, total);
        SizeText = Format.Bytes(bytes);
        PercentText = Format.Percent(Percent);
        Brush = Application.Current.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        ToolTip = $"{name}: {SizeText} ({PercentText})\n{description}";
    }

    public string Name { get; }
    public string Description { get; }
    public long Bytes { get; }
    public double Weight => Bytes;
    public double Percent { get; }
    public string SizeText { get; }
    public string PercentText { get; }
    public string ValueText => $"{SizeText} · {PercentText}";
    public Brush Brush { get; }
    public string ToolTip { get; }
    public bool IsEmpty => Bytes <= 0;
}
