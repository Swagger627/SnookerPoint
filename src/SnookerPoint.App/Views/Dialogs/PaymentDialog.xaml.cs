using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class PaymentDialog : Window
{
    public PaymentDialog(PaymentDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnComplete(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaymentDialogViewModel vm && vm.TryComplete())
        {
            DialogResult = true;
        }
    }
}
