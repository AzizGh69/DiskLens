using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskLens.ViewModels;

namespace DiskLens.Views;

public partial class DriveView : UserControl
{
    public DriveView() => InitializeComponent();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView list || !IsOnRow(e.OriginalSource as DependencyObject)) return;
        if (DataContext is DriveViewModel vm && list.SelectedItem is IPathItem item) vm.OpenItem(item);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListView list) return;
        if (DataContext is DriveViewModel vm && list.SelectedItem is IPathItem item)
        {
            vm.OpenItem(item);
            e.Handled = true;
        }
    }

    /// <summary>True when the double-click landed on a row (not the header or scrollbar).</summary>
    private static bool IsOnRow(DependencyObject? source)
    {
        var d = source;
        while (d is not null)
        {
            if (d is ListViewItem) return true;
            if (d is ListView) return false;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return false;
    }
}
