using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using DiskLens.Core.Services;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object? _selectedPage;
    private bool _isLoading = true;
    private string? _bannerText;
    private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public MainViewModel()
    {
        Overview = new OverviewViewModel(this);
        DismissBannerCommand = RelayCommand.From(() => BannerText = null);
        OpenRevoCommand = RelayCommand.From(OpenRevo);
        _bannerTimer.Tick += (_, _) => { _bannerTimer.Stop(); BannerText = null; };
    }

    public OverviewViewModel Overview { get; }

    // ---- Installed apps & Revo Uninstaller --------------------------------------------------

    private RevoInstallation? _revo;

    /// <summary>Installed programs from the registry, read once at startup.</summary>
    public IReadOnlyList<InstalledApp> InstalledApps { get; private set; } = Array.Empty<InstalledApp>();

    public RevoInstallation? Revo
    {
        get => _revo;
        private set
        {
            if (SetProperty(ref _revo, value))
            {
                OnPropertyChanged(nameof(HasRevo));
                OnPropertyChanged(nameof(RevoButtonText));
                OnPropertyChanged(nameof(RevoCaption));
                OnPropertyChanged(nameof(UninstallActionText));
            }
        }
    }

    public bool HasRevo => Revo is not null;
    public string RevoButtonText => HasRevo ? "Open Revo Uninstaller" : "Open Installed apps";
    public string UninstallActionText => HasRevo ? "Uninstall with Revo" : "Uninstall…";

    public string RevoCaption => Revo switch
    {
        null => "Revo Uninstaller wasn't found, so Windows' Installed apps page opens instead.",
        { IsPro: true } r => $"Revo Uninstaller Pro {ShortVersion(r.Version)}".TrimEnd(),
        var r => $"Revo Uninstaller {ShortVersion(r.Version)} (free)".Replace("  ", " "),
    };

    public ICommand OpenRevoCommand { get; }

    private static string ShortVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    private void OpenRevo()
    {
        if (Revo is { } revo) Shell.Start(revo.ExePath);
        else Shell.OpenUri("ms-settings:appsfeatures");
    }

    /// <summary>
    /// Hands an app to Revo Uninstaller: opens Revo and puts the app's name on the clipboard so it can be
    /// pasted into Revo's search box. Nothing is uninstalled by DiskLens itself.
    /// </summary>
    public void UninstallWithRevo(InstalledApp app)
    {
        bool copied = Shell.CopyText(app.Name);
        string paste = copied ? $"\"{app.Name}\" is copied — press Ctrl+V in the search box, then choose Uninstall." : $"Search for \"{app.Name}\", then choose Uninstall.";

        if (Revo is { } revo)
        {
            if (!Shell.Start(revo.ExePath)) return;
            string where = app.IsStoreApp ? " Store apps are on Revo's \"Windows Apps\" tab." : "";
            ShowBanner($"Revo Uninstaller is open. {paste}{where}");
        }
        else
        {
            Shell.OpenUri("ms-settings:appsfeatures");
            ShowBanner($"Revo Uninstaller wasn't found, so Windows' Installed apps page is open. {paste}");
        }
    }

    /// <summary>Sidebar entries: the overview page followed by one page per drive.</summary>
    public ObservableCollection<object> Pages { get; } = new();

    public object? SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public bool IsScanningAny => Pages.OfType<DriveViewModel>().Any(d => d.IsScanning);

    public string? BannerText
    {
        get => _bannerText;
        private set
        {
            if (SetProperty(ref _bannerText, value)) OnPropertyChanged(nameof(HasBanner));
        }
    }

    public bool HasBanner => !string.IsNullOrEmpty(BannerText);

    public ICommand DismissBannerCommand { get; }

    public async Task InitializeAsync()
    {
        var appsTask = Task.Run(() =>
        {
            var apps = InstalledAppsService.Load();
            return (Apps: apps, Revo: RevoLocator.Find(apps));
        });
        var snapshot = await Task.Run(DiskInfoService.Load);
        (var installed, var revo) = await appsTask;
        InstalledApps = installed;
        Revo = revo;

        var drives = snapshot.Drives
            .OrderByDescending(d => d.IsWindowsDrive)
            .ThenBy(d => d.Letter)
            .Select(d => new DriveViewModel(d, this))
            .ToList();

        Overview.Load(snapshot, drives);
        Pages.Clear();
        Pages.Add(Overview);
        foreach (var d in drives) Pages.Add(d);

        SelectedPage = Overview;
        IsLoading = false;
    }

    public void ShowDrive(DriveViewModel drive, bool startScan = false)
    {
        SelectedPage = drive;
        if (startScan) drive.StartScan();
    }

    public void ScanAll()
    {
        var first = Pages.OfType<DriveViewModel>().FirstOrDefault();
        if (first is null) return;
        SelectedPage = first;
        _ = ScanAllAsync();
    }

    private async Task ScanAllAsync()
    {
        foreach (var drive in Pages.OfType<DriveViewModel>().ToList())
            await drive.ScanAsync();
    }

    public void NotifyScanStateChanged()
    {
        OnPropertyChanged(nameof(IsScanningAny));
        CommandManager.InvalidateRequerySuggested();
    }

    public void ShowBanner(string text)
    {
        BannerText = text;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }
}
