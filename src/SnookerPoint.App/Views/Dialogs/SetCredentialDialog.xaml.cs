using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class SetCredentialDialog : Window
{
    public SetCredentialDialog(SetCredentialDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetCredentialDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
