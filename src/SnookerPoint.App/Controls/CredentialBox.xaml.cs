using System.Windows;
using System.Windows.Controls;

namespace SnookerPoint.App.Controls;

/// <summary>
/// A credential entry control with a single authoritative in-memory value
/// (<see cref="Password"/>) and a robust show/hide (<see cref="Reveal"/>).
/// </summary>
/// <remarks>
/// The inner <see cref="PasswordBox"/> (masked) and <see cref="TextBox"/> (plain)
/// are always kept in sync with the authoritative value, so toggling reveal never
/// clears or loses what the user typed. Change handlers are attached in the
/// constructor — not lazily — which fixes the previous defect where keystrokes did
/// not reach the view model when the bound value started empty. A re-entrancy guard
/// prevents feedback loops. The plaintext is never written to logs or the database.
/// </remarks>
public partial class CredentialBox : UserControl
{
    private bool _syncing;

    public CredentialBox()
    {
        InitializeComponent();

        // Always-live handlers: keystrokes propagate regardless of initial value.
        Pb.PasswordChanged += (_, _) => OnInnerValueChanged(Pb.Password);
        Tb.TextChanged += (_, _) => OnInnerValueChanged(Tb.Text);
    }

    /// <summary>The authoritative credential value (two-way by default).</summary>
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.Register(
            nameof(Password),
            typeof(string),
            typeof(CredentialBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordPropertyChanged));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    /// <summary>When true the value is shown as plain text; otherwise it is masked.</summary>
    public static readonly DependencyProperty RevealProperty =
        DependencyProperty.Register(
            nameof(Reveal),
            typeof(bool),
            typeof(CredentialBox),
            new FrameworkPropertyMetadata(false, OnRevealPropertyChanged));

    public bool Reveal
    {
        get => (bool)GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    /// <summary>Name used in accessibility text (e.g. "password" or "PIN").</summary>
    public static readonly DependencyProperty SecretNameProperty =
        DependencyProperty.Register(
            nameof(SecretName),
            typeof(string),
            typeof(CredentialBox),
            new PropertyMetadata("password"));

    public string SecretName
    {
        get => (string)GetValue(SecretNameProperty);
        set => SetValue(SecretNameProperty, value);
    }

    private void OnInnerValueChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            // SetCurrentValue updates the value (and pushes to the two-way binding
            // source) without replacing the binding.
            if (Password != value)
            {
                SetCurrentValue(PasswordProperty, value);
            }

            if (Pb.Password != value)
            {
                Pb.Password = value;
            }

            if (Tb.Text != value)
            {
                Tb.Text = value;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CredentialBox)d;
        if (control._syncing)
        {
            return;
        }

        control._syncing = true;
        try
        {
            var value = (string)(e.NewValue ?? string.Empty);
            if (control.Pb.Password != value)
            {
                control.Pb.Password = value;
            }

            if (control.Tb.Text != value)
            {
                control.Tb.Text = value;
            }
        }
        finally
        {
            control._syncing = false;
        }
    }

    private static void OnRevealPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CredentialBox)d).ApplyReveal((bool)e.NewValue);

    private void ApplyReveal(bool reveal)
    {
        // Both inner controls already hold the value, so this only swaps which one
        // is visible and moves focus/caret — it never touches the value itself.
        if (reveal)
        {
            Tb.Visibility = Visibility.Visible;
            Pb.Visibility = Visibility.Collapsed;
            if (Pb.IsKeyboardFocusWithin)
            {
                Tb.Focus();
                Tb.CaretIndex = Tb.Text.Length;
            }
        }
        else
        {
            Pb.Visibility = Visibility.Visible;
            Tb.Visibility = Visibility.Collapsed;
            if (Tb.IsKeyboardFocusWithin)
            {
                // Focusing the PasswordBox places the caret at the end by default.
                Pb.Focus();
            }
        }
    }
}
