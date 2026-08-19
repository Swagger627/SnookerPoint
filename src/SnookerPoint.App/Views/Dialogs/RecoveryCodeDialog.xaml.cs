using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SnookerPoint.App.Views.Dialogs;

/// <summary>Shows a one-time recovery code with copy and print options. Never logs it.</summary>
public partial class RecoveryCodeDialog : Window
{
    private readonly string _code;

    public RecoveryCodeDialog(string code)
    {
        InitializeComponent();
        _code = code;
        DataContext = code;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_code);
            MessageBox.Show(this, "Recovery code copied to the clipboard.", "Copied",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            // Clipboard can transiently fail; the code is still shown on screen.
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var doc = new FlowDocument(new Paragraph(new Run("Snooker Point — Owner recovery code\n\n"))
        {
            FontSize = 16,
        });
        doc.Blocks.Add(new Paragraph(new Run(_code)) { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 22, FontWeight = FontWeights.Bold });
        doc.Blocks.Add(new Paragraph(new Run("Keep this code somewhere safe. It is the only way to recover the Owner account offline.")));

        IDocumentPaginatorSource source = doc;
        dialog.PrintDocument(source.DocumentPaginator, "Snooker Point recovery code");
    }
}
