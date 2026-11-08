using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DiskLens.Theme;

/// <summary>
/// Chart colors for light and dark mode. The four category hues are the first four slots of a
/// colorblind-validated categorical palette (checked against both Fluent card surfaces); the
/// legend always carries the text labels, so color is never the only cue.
/// </summary>
public static class ChartColors
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        // Category slots, in fixed order (System, Apps, Your files, Other)
        ["Cat.System"] = ("#2A78D6", "#3987E5"),
        ["Cat.Apps"] = ("#EB6834", "#D95926"),
        ["Cat.User"] = ("#1BAF7A", "#199E70"),
        ["Cat.Other"] = ("#EDA100", "#C98500"),
        ["Cat.Unreadable"] = ("#898781", "#898781"),
        ["Cat.Free"] = ("#E1E0D9", "#45443F"),

        // Used / free capacity bars (one hue: used = solid, free = light tint)
        ["Bar.Used"] = ("#2A78D6", "#3987E5"),
        ["Bar.Free"] = ("#CDE2FB", "#1C3F6B"),
        ["Bar.Hidden"] = ("#C3C2B7", "#5A5955"),
        ["Bar.Track"] = ("#E1E0D9", "#383835"),

        // Status (always paired with an icon + label)
        ["Status.Critical"] = ("#D03B3B", "#D03B3B"),
        ["Status.Warning"] = ("#FAB219", "#FAB219"),
        ["Status.Good"] = ("#0CA30C", "#0CA30C"),
    };

    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(Application app)
    {
        bool dark = IsDarkMode();
        foreach (var (key, (light, darkHex)) in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkHex : light));
            brush.Freeze();
            app.Resources[key] = brush;
        }
    }

    public static void FollowSystemTheme(Application app)
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                app.Dispatcher.BeginInvoke(new Action(() => Apply(app)));
        };
    }
}
