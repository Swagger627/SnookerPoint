using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class DiscountDialog : Window
{
    public DiscountDialog(DiscountDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscountDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
