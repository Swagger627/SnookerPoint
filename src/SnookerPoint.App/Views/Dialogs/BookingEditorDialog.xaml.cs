using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class BookingEditorDialog : Window
{
    public BookingEditorDialog(BookingEditorDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is BookingEditorDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
