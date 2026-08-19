using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Tables;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>A rate-segment row for the finish summary.</summary>
public sealed record FinishSegmentRow(string TableName, string RateText, string DurationText);

/// <summary>Backs the Finish Session dialog (summary + closing note).</summary>
public partial class FinishDialogViewModel : ObservableObject
{
    public FinishDialogViewModel(SessionSummary summary)
    {
        SessionNumber = summary.SessionNumber;
        StartText = DisplayFormat.LocalDateTime(summary.StartUtc);
        FinishText = summary.FinishUtc is { } f ? DisplayFormat.LocalDateTime(f) : DisplayFormat.LocalDateTime(DateTimeOffset.UtcNow);
        ElapsedText = DisplayFormat.DurationShort(summary.ElapsedSeconds);
        PausedText = DisplayFormat.DurationShort(summary.PausedSeconds);
        BillableText = DisplayFormat.DurationShort(summary.BillableSeconds);
        ChargeText = summary.Charge.Format();
        CustomerLabel = summary.CustomerLabel;
        HasMultipleSegments = summary.Segments.Count > 1;

        Segments = new ObservableCollection<FinishSegmentRow>(
            summary.Segments.Select(s => new FinishSegmentRow(
                s.TableName,
                $"{s.HourlyRate.Format()}/hr",
                DisplayFormat.DurationShort(s.ActiveSeconds))));
    }

    public int SessionNumber { get; }
    public string StartText { get; }
    public string FinishText { get; }
    public string ElapsedText { get; }
    public string PausedText { get; }
    public string BillableText { get; }
    public string ChargeText { get; }
    public string? CustomerLabel { get; }
    public bool HasMultipleSegments { get; }
    public ObservableCollection<FinishSegmentRow> Segments { get; }

    [ObservableProperty] private string _closingNote = string.Empty;

    public FinishInput? Result { get; private set; }

    public bool TryConfirm()
    {
        Result = new FinishInput(string.IsNullOrWhiteSpace(ClosingNote) ? null : ClosingNote.Trim());
        return true;
    }
}
