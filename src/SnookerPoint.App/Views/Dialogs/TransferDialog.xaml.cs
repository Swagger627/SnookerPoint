using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class TransferDialog : Window
{
    public TransferDialog(TransferDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => ReasonBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is TransferDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
