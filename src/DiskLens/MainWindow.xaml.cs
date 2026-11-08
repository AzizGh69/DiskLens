using System.Security.Principal;
using System.Windows;

namespace DiskLens;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Title = isAdmin ? "DiskLens — Administrator" : "DiskLens";
        AdminText.Text = isAdmin ? "Running as administrator" : "Limited mode — restart as administrator";
    }
}
