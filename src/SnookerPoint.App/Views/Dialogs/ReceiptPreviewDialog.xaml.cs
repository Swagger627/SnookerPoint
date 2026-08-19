using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnookerPoint.App.Views.Dialogs;

/// <summary>
/// Shows the monospace receipt snapshot and prints it via the Windows print dialog. A
/// printer failure never throws to the caller — the sale is already complete; the user
/// sees an error and can retry. <see cref="Printed"/> reports whether a print succeeded.
/// </summary>
public partial class ReceiptPreviewDialog : Window
{
    private readonly string _receiptText;

    public ReceiptPreviewDialog(string title, string receiptText)
    {
        InitializeComponent();
        _receiptText = receiptText;
        TitleText.Text = title;
        ReceiptText.Text = receiptText;
    }

    /// <summary>True when at least one print succeeded during this dialog.</summary>
    public bool Printed { get; private set; }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PagePadding = new Thickness(24),
                ColumnWidth = double.PositiveInfinity,
            };
            doc.Blocks.Add(new Paragraph(new Run(_receiptText)));

            IDocumentPaginatorSource source = doc;
            dialog.PrintDocument(source.DocumentPaginator, "Snooker Point receipt");
            Printed = true;
            StatusText.Text = "Receipt sent to printer.";
        }
        catch
        {
            // Printing must never undo the completed sale.
            StatusText.Text = "The receipt could not be printed. The sale was still completed. You can try again.";
        }
    }
}
