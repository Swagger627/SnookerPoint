using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class ForgotPasswordDialog : Window
{
    public ForgotPasswordDialog(ForgotPasswordDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is ForgotPasswordDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
