using System.Windows;
using Microsoft.Win32;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class ProductEditorDialog : Window
{
    public ProductEditorDialog(ProductEditorDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => BarcodeBox.Focus();
    }

    private void OnChooseImage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a product image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp",
        };

        if (dialog.ShowDialog(this) == true && DataContext is ProductEditorDialogViewModel vm)
        {
            vm.SetNewImage(dialog.FileName);
        }
    }

    private void OnRemoveImage(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductEditorDialogViewModel vm)
        {
            vm.RemoveImage();
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductEditorDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
