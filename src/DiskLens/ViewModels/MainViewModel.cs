using System;
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
        _bannerTimer.Tick += (_, _) => { _bannerTimer.Stop(); BannerText = null; };
    }

    public OverviewViewModel Overview { get; }

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
        var snapshot = await Task.Run(DiskInfoService.Load);

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
