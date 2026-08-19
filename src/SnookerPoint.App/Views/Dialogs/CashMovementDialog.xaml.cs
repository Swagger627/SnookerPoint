using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class CashMovementDialog : Window
{
    public CashMovementDialog(CashMovementDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => AmountBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is CashMovementDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
