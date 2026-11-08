using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using DiskLens.ViewModels;

namespace DiskLens;

public partial class MainWindow : Window
{
    private readonly TaskCompletionSource _ready = new();

    public MainWindow()
    {
        InitializeComponent();

        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Title = isAdmin ? "DiskLens — Administrator" : "DiskLens";
        AdminText.Text = isAdmin ? "Running as administrator" : "Limited mode (not administrator)";

        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            _ready.TrySetResult();
        };
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Completes once disks and drives have been loaded.</summary>
    public Task Ready => _ready.Task;
}
