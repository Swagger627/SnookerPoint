using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class BillingSettingsDialog : Window
{
    public BillingSettingsDialog(BillingSettingsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is BillingSettingsDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
