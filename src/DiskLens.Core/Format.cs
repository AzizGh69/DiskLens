using System;
using System.Globalization;

namespace DiskLens.Core;

/// <summary>Human-friendly formatting for sizes, counts and percentages.</summary>
public static class Format
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Formats a byte count the way Explorer does (1 KB = 1024 bytes).</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0) return $"{bytes} B";
        string number = value >= 100 ? value.ToString("0", CultureInfo.CurrentCulture)
                      : value >= 10 ? value.ToString("0.0", CultureInfo.CurrentCulture)
                      : value.ToString("0.00", CultureInfo.CurrentCulture);
        return $"{number} {Units[unit]}";
    }

    /// <summary>Formats a 0–100 percentage.</summary>
    public static string Percent(double percent)
    {
        if (double.IsNaN(percent) || percent <= 0) return "0%";
        if (percent < 0.1) return "<0.1%";
        return percent.ToString(percent >= 10 ? "0" : "0.#", CultureInfo.CurrentCulture) + "%";
    }

    public static string Count(long count) => count.ToString("N0", CultureInfo.CurrentCulture);

    public static string Duration(TimeSpan span) =>
        span.TotalSeconds < 60 ? $"{span.TotalSeconds:0} s" : $"{(int)span.TotalMinutes} min {span.Seconds} s";

    /// <summary>Share of <paramref name="part"/> in <paramref name="whole"/> as 0–100.</summary>
    public static double Share(long part, long whole) => whole <= 0 ? 0 : Math.Clamp(part * 100.0 / whole, 0, 100);
}
