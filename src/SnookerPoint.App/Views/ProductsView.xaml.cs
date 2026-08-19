using System.Windows.Controls;
using System.Windows.Input;
using SnookerPoint.App.ViewModels;

namespace SnookerPoint.App.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    /// <summary>
    /// USB barcode scanners type the code then send Enter — treat Enter in the search
    /// box as a scan lookup.
    /// </summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ProductsViewModel vm)
        {
            vm.ScanCommand.Execute(null);
            e.Handled = true;
        }
    }
}
