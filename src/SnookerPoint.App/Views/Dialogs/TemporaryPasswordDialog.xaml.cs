using System.Windows;

namespace SnookerPoint.App.Views.Dialogs;

/// <summary>
/// Shows a one-time temporary password for a staff member with a copy option. The value
/// is displayed once and never written to logs or audit details.
/// </summary>
public partial class TemporaryPasswordDialog : Window
{
    private readonly string _password;

    public TemporaryPasswordDialog(string staffName, string temporaryPassword)
    {
        InitializeComponent();
        _password = temporaryPassword;
        TitleText.Text = $"Temporary password for {staffName}";
        SecretText.Text = temporaryPassword;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_password);
            MessageBox.Show(this, "Temporary password copied to the clipboard.", "Copied",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            // Clipboard can transiently fail; the password is still shown on screen.
        }
    }
}
