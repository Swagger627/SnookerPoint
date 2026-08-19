using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class StockMovementDialog : Window
{
    public StockMovementDialog(StockMovementDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => QtyBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is StockMovementDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
