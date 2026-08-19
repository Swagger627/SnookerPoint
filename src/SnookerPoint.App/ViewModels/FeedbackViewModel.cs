using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SnookerPoint.App.ViewModels;

/// <summary>The severity of a feedback message, driving the banner's icon and colour.</summary>
public enum FeedbackKind
{
    Success,
    Warning,
    Error,
}

/// <summary>
/// A small, reusable feedback surface a screen owns and shows through the themed
/// <c>FeedbackBanner</c> control. View models call <see cref="Success"/>,
/// <see cref="Warning"/> or <see cref="Error"/> to raise a clearly-visible, icon+text
/// message that is readable in both themes and dismissible. It never carries secrets —
/// callers pass only friendly, non-sensitive text.
/// </summary>
public partial class FeedbackViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private string? _message;

    [ObservableProperty]
    private FeedbackKind _kind;

    /// <summary>True when there is a message to show.</summary>
    public bool IsVisible => !string.IsNullOrWhiteSpace(Message);

    public void Success(string message) => Set(FeedbackKind.Success, message);

    public void Warning(string message) => Set(FeedbackKind.Warning, message);

    public void Error(string? message) =>
        Set(FeedbackKind.Error, string.IsNullOrWhiteSpace(message)
            ? "Something went wrong. Please try again."
            : message);

    /// <summary>Clears the current message so the banner hides.</summary>
    public void Clear() => Message = null;

    [RelayCommand]
    private void Dismiss() => Clear();

    private void Set(FeedbackKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }
}
