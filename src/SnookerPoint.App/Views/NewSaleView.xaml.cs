using System.Windows.Controls;
using System.Windows.Input;
using SnookerPoint.App.ViewModels;

namespace SnookerPoint.App.Views;

public partial class NewSaleView : UserControl
{
    private NewSaleViewModel? _vm;

    public NewSaleView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += (_, _) => Hook();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Hook();
        SearchBox.Focus();
    }

    private void Hook()
    {
        if (_vm is not null)
        {
            _vm.FocusSearchRequested -= FocusSearch;
        }

        _vm = DataContext as NewSaleViewModel;
        if (_vm is not null)
        {
            _vm.FocusSearchRequested += FocusSearch;
        }
    }

    private void FocusSearch() => SearchBox.Focus();

    /// <summary>USB scanners type the code then send Enter — treat Enter as a scan.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is NewSaleViewModel vm)
        {
            vm.ScanCommand.Execute(null);
            e.Handled = true;
        }
    }
}
