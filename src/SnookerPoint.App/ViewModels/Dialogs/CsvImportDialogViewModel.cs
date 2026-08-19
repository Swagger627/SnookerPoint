using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.Application.Catalog;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the CSV import preview: per-row validation and the duplicate strategy.</summary>
public partial class CsvImportDialogViewModel : ObservableObject
{
    public CsvImportDialogViewModel(CsvImportPreview preview)
    {
        Preview = preview;
        Rows = preview.Rows.Select(r => new CsvRowDisplay(
            r.LineNumber, r.Sku, r.Name,
            r.IsValid ? "OK" : "Problem",
            r.IsValid,
            r.Errors.Count > 0 ? string.Join("; ", r.Errors) : (r.DuplicateSku || r.DuplicateBarcode ? "Already exists" : "—")))
            .ToList();
    }

    public CsvImportPreview Preview { get; }

    public IReadOnlyList<CsvRowDisplay> Rows { get; }

    public IReadOnlyList<string> FileErrors => Preview.FileErrors;
    public bool HasFileErrors => Preview.HasFileErrors;

    public string SummaryText => Preview.HasFileErrors
        ? "The file could not be read."
        : $"{Preview.ValidCount} valid · {Preview.InvalidCount} with problems · {Preview.DuplicateCount} already exist";

    public bool CanImport => Preview.CanImport;

    // Duplicate strategy (Skip by default; never silently replaces prices).
    [ObservableProperty] private bool _skip = true;
    [ObservableProperty] private bool _updateBySku;
    [ObservableProperty] private bool _updateByBarcode;

    private bool _switching;

    partial void OnSkipChanged(bool value) => Exclusive(value, () => { UpdateBySku = false; UpdateByBarcode = false; });
    partial void OnUpdateBySkuChanged(bool value) => Exclusive(value, () => { Skip = false; UpdateByBarcode = false; });
    partial void OnUpdateByBarcodeChanged(bool value) => Exclusive(value, () => { Skip = false; UpdateBySku = false; });

    private void Exclusive(bool value, Action clearOthers)
    {
        if (!value || _switching)
        {
            return;
        }

        _switching = true;
        clearOthers();
        _switching = false;
    }

    public CsvDuplicateStrategy Strategy =>
        UpdateBySku ? CsvDuplicateStrategy.UpdateBySku
        : UpdateByBarcode ? CsvDuplicateStrategy.UpdateByBarcode
        : CsvDuplicateStrategy.Skip;

    public CsvDuplicateStrategy? Result { get; private set; }

    public bool Confirm()
    {
        if (!CanImport)
        {
            return false;
        }

        Result = Strategy;
        return true;
    }
}

/// <summary>A single CSV preview row for the grid.</summary>
public sealed record CsvRowDisplay(int LineNumber, string Sku, string Name, string StatusText, bool IsValid, string Detail);
