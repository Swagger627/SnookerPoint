using System.Windows.Controls;

namespace SnookerPoint.App.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameBox.Focus();
    }
}
