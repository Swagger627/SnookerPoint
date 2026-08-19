using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class PriceOverrideDialog : Window
{
    public PriceOverrideDialog(PriceOverrideDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => PriceBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is PriceOverrideDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
