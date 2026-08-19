using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnookerPoint.App.Controls;

/// <summary>
/// One reusable, controlled mouse-wheel scrolling behaviour for <see cref="ScrollViewer"/>.
/// Attach it with <c>ctrl:SmoothScrollBehavior.Enabled="True"</c>.
/// </summary>
/// <remarks>
/// WPF's default wheel step can jump a whole item (a full card) at a time when content is
/// item-scrolled, which feels wildly over-sensitive. This behaviour instead scrolls a fixed,
/// predictable number of pixels per wheel notch. It cooperates with nested scrolling: because
/// <see cref="ScrollViewer.PreviewMouseWheel"/> tunnels from the outermost container inward,
/// the handler first checks whether an inner scroll region under the pointer can still scroll
/// in the wheel's direction — if so it lets that inner control handle it (so a DataGrid or an
/// inner list scrolls normally), and only takes over at the inner boundary. Trackpads (which
/// send many small deltas) stay smooth because the step is proportional to the raw delta.
/// Keyboard scrolling (arrows, Page Up/Down, Home/End) is left to the ScrollViewer's own
/// defaults and is unaffected.
/// </remarks>
public static class SmoothScrollBehavior
{
    /// <summary>Pixels scrolled for one full wheel notch (a 120-unit delta).</summary>
    private const double PixelsPerNotch = 50;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = (ScrollViewer)sender;

        if (e.Handled || e.Delta == 0)
        {
            return;
        }

        // Nothing to scroll here — let the event keep bubbling to an outer container.
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        // If an inner scroll region under the pointer can still scroll this way, defer to it.
        var inner = FindInnerScrollViewer(e.OriginalSource as DependencyObject, scrollViewer);
        if (inner is not null && CanScrollInDirection(inner, e.Delta))
        {
            return;
        }

        var target = scrollViewer.VerticalOffset - (e.Delta / 120.0 * PixelsPerNotch);
        target = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, target));
        scrollViewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    /// <summary>Finds the innermost ScrollViewer between the event source and the owner, exclusive of the owner.</summary>
    private static ScrollViewer? FindInnerScrollViewer(DependencyObject? source, ScrollViewer owner)
    {
        var current = source;
        while (current is not null && current != owner)
        {
            if (current is ScrollViewer inner && inner != owner)
            {
                return inner;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool CanScrollInDirection(ScrollViewer scrollViewer, int delta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        // delta > 0 scrolls up (toward offset 0); delta < 0 scrolls down.
        return delta > 0
            ? scrollViewer.VerticalOffset > 0
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }
}
