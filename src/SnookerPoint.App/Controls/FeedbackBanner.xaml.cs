using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnookerPoint.App.ViewModels;

namespace SnookerPoint.App.Controls;

/// <summary>
/// A themed success / warning / error banner with an icon and text. Colour is always
/// paired with an icon and words (never colour alone), it reads correctly in dark and
/// light themes, and it can be dismissed. It self-collapses when <see cref="Message"/> is
/// empty, so a screen can bind it permanently and it only appears when there is something
/// to say.
/// </summary>
public partial class FeedbackBanner : UserControl
{
    public FeedbackBanner()
    {
        InitializeComponent();
    }

    /// <summary>The severity, driving the icon glyph and semantic colour.</summary>
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(FeedbackKind),
            typeof(FeedbackBanner),
            new PropertyMetadata(FeedbackKind.Success));

    public FeedbackKind Kind
    {
        get => (FeedbackKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The message to show. When null/empty the banner is collapsed.</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(FeedbackBanner),
            new PropertyMetadata(null));

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Invoked when the user clicks the dismiss (×) button.</summary>
    public static readonly DependencyProperty DismissCommandProperty =
        DependencyProperty.Register(
            nameof(DismissCommand),
            typeof(ICommand),
            typeof(FeedbackBanner),
            new PropertyMetadata(null));

    public ICommand? DismissCommand
    {
        get => (ICommand?)GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    /// <summary>Whether the dismiss (×) button is shown.</summary>
    public static readonly DependencyProperty ShowDismissProperty =
        DependencyProperty.Register(
            nameof(ShowDismiss),
            typeof(bool),
            typeof(FeedbackBanner),
            new PropertyMetadata(true));

    public bool ShowDismiss
    {
        get => (bool)GetValue(ShowDismissProperty);
        set => SetValue(ShowDismissProperty, value);
    }
}
