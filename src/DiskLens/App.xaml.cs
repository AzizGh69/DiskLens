using System.Windows;
using DiskLens.Theme;

namespace DiskLens;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ChartColors.Apply(this);
        ChartColors.FollowSystemTheme(this);
        base.OnStartup(e);
    }
}
