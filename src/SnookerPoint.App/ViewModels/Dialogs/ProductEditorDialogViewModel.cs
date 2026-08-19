using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>
/// Backs the product editor (add / edit / duplicate). Validates a required name and SKU,
/// a required non-negative price, an optional non-negative cost, and non-negative reorder
/// and opening quantities. Money is parsed into <see cref="Money"/> (integer paisa).
/// </summary>
public partial class ProductEditorDialogViewModel : ObservableObject
{
    public ProductEditorDialogViewModel(ProductEditorContext context)
    {
        Title = context.Title;
        IsNew = context.IsNew;
        Categories = context.Categories;

        var existing = context.Existing;
        if (existing is not null)
        {
            Name = existing.Name;
            // A duplicate starts from the source but with a fresh identity.
            Sku = context.IsNew ? string.Empty : existing.Sku;
            Barcode = context.IsNew ? string.Empty : existing.Barcode ?? string.Empty;
            Brand = existing.Brand ?? string.Empty;
            Variant = existing.Variant ?? string.Empty;
            Size = existing.Size ?? string.Empty;
            SelectedUnit = existing.Unit;
            CostRupees = existing.Cost is { } c ? c.ToRupees().ToString(CultureInfo.CurrentCulture) : string.Empty;
            PriceRupees = existing.Price.ToRupees().ToString(CultureInfo.CurrentCulture);
            TrackInventory = existing.TrackInventory;
            AllowNegativeStock = existing.AllowNegativeStock;
            ReorderLevelText = existing.ReorderLevel.ToString(CultureInfo.CurrentCulture);
            Notes = existing.Notes ?? string.Empty;
            _existingImagePath = context.IsNew ? null : existing.ImagePath;
            ImagePreviewPath = context.ExistingImageFullPath;
        }

        if (!string.IsNullOrWhiteSpace(context.PrefillBarcode))
        {
            Barcode = context.PrefillBarcode!;
        }

        SelectedCategory = Categories.FirstOrDefault(c => existing is not null && c.Id == existing.CategoryId)
            ?? Categories.FirstOrDefault();
    }

    public string Title { get; }
    public bool IsNew { get; }
    public IReadOnlyList<CategoryOption> Categories { get; }
    public Array Units => Enum.GetValues(typeof(ProductUnit));

    /// <summary>Opening stock is only offered for a new, inventory-tracked product.</summary>
    public bool ShowOpeningStock => IsNew && TrackInventory;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _sku = string.Empty;
    [ObservableProperty] private string _barcode = string.Empty;
    [ObservableProperty] private CategoryOption? _selectedCategory;
    [ObservableProperty] private string _brand = string.Empty;
    [ObservableProperty] private string _variant = string.Empty;
    [ObservableProperty] private string _size = string.Empty;
    [ObservableProperty] private ProductUnit _selectedUnit = ProductUnit.Each;
    [ObservableProperty] private string _costRupees = string.Empty;
    [ObservableProperty] private string _priceRupees = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOpeningStock))]
    private bool _trackInventory = true;

    [ObservableProperty] private bool _allowNegativeStock;
    [ObservableProperty] private string _reorderLevelText = "0";
    [ObservableProperty] private string _openingQuantityText = "0";
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private string? _imagePreviewPath;
    [ObservableProperty] private string? _errorMessage;

    private string? _existingImagePath;
    private ProductImageAction _imageAction = ProductImageAction.Keep;
    private string? _newImageSourcePath;

    public bool HasImagePreview => !string.IsNullOrWhiteSpace(ImagePreviewPath);

    partial void OnImagePreviewPathChanged(string? value) => OnPropertyChanged(nameof(HasImagePreview));

    /// <summary>Called by the view when the user picks a new image file.</summary>
    public void SetNewImage(string sourcePath)
    {
        _imageAction = ProductImageAction.Replace;
        _newImageSourcePath = sourcePath;
        ImagePreviewPath = sourcePath;
    }

    /// <summary>Called by the view when the user removes the image.</summary>
    public void RemoveImage()
    {
        _imageAction = _existingImagePath is null ? ProductImageAction.Keep : ProductImageAction.Remove;
        _newImageSourcePath = null;
        ImagePreviewPath = null;
    }

    public ProductEditorResult? Result { get; private set; }

    public bool TryConfirm()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Please enter a product name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Sku))
        {
            ErrorMessage = "Please enter an internal SKU.";
            return false;
        }

        if (SelectedCategory is null)
        {
            ErrorMessage = "Please choose a category.";
            return false;
        }

        if (!MoneyInput.TryParseRupees(PriceRupees, out var price))
        {
            ErrorMessage = "Enter a valid selling price in Rs (0 or more).";
            return false;
        }

        Money? cost = null;
        if (!string.IsNullOrWhiteSpace(CostRupees))
        {
            if (!MoneyInput.TryParseRupees(CostRupees, out var c))
            {
                ErrorMessage = "Enter a valid purchase cost in Rs (0 or more), or leave it blank.";
                return false;
            }

            cost = c;
        }

        if (!TryParseNonNegative(ReorderLevelText, out var reorder))
        {
            ErrorMessage = "Enter a valid reorder level (0 or more).";
            return false;
        }

        var opening = 0m;
        if (ShowOpeningStock && !string.IsNullOrWhiteSpace(OpeningQuantityText)
            && !TryParseNonNegative(OpeningQuantityText, out opening))
        {
            ErrorMessage = "Enter a valid opening quantity (0 or more).";
            return false;
        }

        Result = new ProductEditorResult(
            Name.Trim(),
            Sku.Trim(),
            string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim(),
            SelectedCategory.Id,
            Blank(Brand), Blank(Variant), Blank(Size),
            SelectedUnit, cost, price, TrackInventory, AllowNegativeStock,
            reorder, opening, Blank(Notes),
            _imageAction, _newImageSourcePath);
        return true;
    }

    private static bool TryParseNonNegative(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value) && value >= 0)
        {
            return true;
        }

        value = 0m;
        return false;
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
