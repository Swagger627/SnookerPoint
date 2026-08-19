using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The Products catalogue screen: search (incl. barcode), category / active / low-stock
/// filters, and add/edit/duplicate/activate plus CSV import/export. All actions are
/// permission-gated and report clear feedback.
/// </summary>
public partial class ProductsViewModel : ObservableObject
{
    private readonly IProductService _products;
    private readonly ICategoryService _categories;
    private readonly IProductCsvService _csv;
    private readonly IProductImageStore _images;
    private readonly AppDataPaths _paths;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;

    private string? _lastScanned;

    public ProductsViewModel(
        IProductService products,
        ICategoryService categories,
        IProductCsvService csv,
        IProductImageStore images,
        AppDataPaths paths,
        ISessionContext session,
        IPermissionService permissions,
        IDialogService dialogs,
        INavigationService navigation,
        IThemeService theme)
    {
        _products = products;
        _categories = categories;
        _csv = csv;
        _images = images;
        _paths = paths;
        _session = session;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _theme = theme;

        ActiveFilters = new[]
        {
            new ActiveFilterOption(ProductActiveFilter.ActiveOnly, "Active only"),
            new ActiveFilterOption(ProductActiveFilter.InactiveOnly, "Inactive only"),
            new ActiveFilterOption(ProductActiveFilter.All, "All"),
        };
        _selectedActiveFilter = ActiveFilters[0];

        LoadCategoryFilters();
        Refresh();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<ProductRowViewModel> Rows { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;

    private int UserId => _session.CurrentUser!.Id;

    public bool CanManage => Has(Permission.ManageProducts);
    public bool CanImport => Has(Permission.ImportProducts);
    public bool CanExport => Has(Permission.ExportProducts);

    public bool IsEmpty => Rows.Count == 0;

    // ---- Filters ----
    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = new();
    public IReadOnlyList<ActiveFilterOption> ActiveFilters { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private CategoryFilterOption? _selectedCategoryFilter;
    [ObservableProperty] private ActiveFilterOption _selectedActiveFilter;
    [ObservableProperty] private bool _lowStockOnly;

    partial void OnSearchTextChanged(string value)
    {
        _lastScanned = null; // typing resets the accidental-rescan guard
        Refresh();
    }

    partial void OnSelectedCategoryFilterChanged(CategoryFilterOption? value) => Refresh();
    partial void OnSelectedActiveFilterChanged(ActiveFilterOption value) => Refresh();
    partial void OnLowStockOnlyChanged(bool value) => Refresh();

    // ---- Commands ----

    [RelayCommand]
    private void Refresh()
    {
        var filter = new ProductFilter(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            SelectedCategoryFilter?.Id,
            SelectedActiveFilter?.Value ?? ProductActiveFilter.ActiveOnly,
            LowStockOnly);

        Rows.Clear();
        foreach (var item in _products.GetList(filter))
        {
            Rows.Add(new ProductRowViewModel(item, _images.GetFullPath(item.ImagePath)));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Enter in the search box: treat the text as a scanned barcode and look it up.</summary>
    [RelayCommand]
    private void Scan()
    {
        var code = SearchText?.Trim();
        if (string.IsNullOrEmpty(code) || code == _lastScanned)
        {
            return; // ignore blank or an accidental repeat submission
        }

        _lastScanned = code;
        var found = _products.FindByBarcode(code);
        if (found is not null)
        {
            Feedback.Success($"Found: {found.Name} ({found.Sku}).");
            return;
        }

        if (CanManage)
        {
            // Offer to add the missing product, prefilled with the scanned barcode.
            OpenEditor(ProductEditorMode.New, prefillBarcode: code);

            // If the editor was cancelled (still no product for this barcode), guide the user.
            if (_products.FindByBarcode(code) is null)
            {
                Feedback.Warning($"No product has barcode {code}. Use Add product to create it.");
            }

            return;
        }

        Feedback.Warning($"No product found for barcode {code}.");
    }

    [RelayCommand]
    private void AddProduct()
    {
        if (CanManage)
        {
            OpenEditor(ProductEditorMode.New);
        }
    }

    [RelayCommand]
    private void Edit(ProductRowViewModel? row)
    {
        if (row is not null && CanManage)
        {
            OpenEditor(ProductEditorMode.Edit, sourceId: row.Id);
        }
    }

    [RelayCommand]
    private void Duplicate(ProductRowViewModel? row)
    {
        if (row is not null && CanManage)
        {
            OpenEditor(ProductEditorMode.Duplicate, sourceId: row.Id);
        }
    }

    [RelayCommand]
    private void ToggleActive(ProductRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var result = _products.SetActive(row.Id, !row.IsActive, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success(row.IsActive ? $"{row.Name} was deactivated." : $"{row.Name} was activated.");
    }

    [RelayCommand]
    private void ImportCsv()
    {
        if (!CanImport)
        {
            return;
        }

        Feedback.Clear();
        var path = _dialogs.PickOpenFile("Choose a product CSV", "CSV files (*.csv)|*.csv|All files (*.*)|*.*", _paths.Exports);
        if (path is null)
        {
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            Feedback.Error("That file could not be read.");
            return;
        }

        var preview = _csv.Preview(content);
        var strategy = _dialogs.ShowCsvImportPreview(preview);
        if (strategy is null)
        {
            return;
        }

        var result = _csv.Import(content, strategy.Value, UserId, null);
        if (result.Failed || result.Value is null)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        LoadCategoryFilters();
        Refresh();
        var r = result.Value;
        Feedback.Success($"Import complete: {r.Added} added, {r.Updated} updated, {r.Skipped} skipped.");
    }

    [RelayCommand]
    private void ExportCatalogue() => Export("products", _csv.ExportProducts());

    [RelayCommand]
    private void ExportStockSummary() => Export("stock-summary", _csv.ExportStockSummary());

    [RelayCommand]
    private void DownloadTemplate() => Export("product-template", _csv.Template());

    private void Export(string kind, string content)
    {
        if (!CanExport)
        {
            return;
        }

        Feedback.Clear();
        var name = $"snookerpoint-{kind}-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        var path = _dialogs.PickSaveFile("Save CSV", name, "CSV files (*.csv)|*.csv", _paths.Exports);
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, content);
            Feedback.Success($"Saved to {path}.");
        }
        catch
        {
            Feedback.Error("The file could not be saved.");
        }
    }

    [RelayCommand]
    private void OpenCategories() => _navigation.ShowCategories();

    [RelayCommand]
    private void OpenInventory() => _navigation.ShowInventory();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    // ---- Helpers ----

    private enum ProductEditorMode { New, Edit, Duplicate }

    private void OpenEditor(ProductEditorMode mode, int? sourceId = null, string? prefillBarcode = null)
    {
        Feedback.Clear();

        var options = _categories.GetAll(includeInactive: false)
            .Select(c => new CategoryOption(c.Id, c.Name)).ToList();
        if (options.Count == 0)
        {
            Feedback.Warning("Please create a category first.");
            return;
        }

        ProductDetail? existing = null;
        if (sourceId is { } id)
        {
            existing = _products.Get(id);
            if (existing is null)
            {
                Feedback.Error("That product was not found.");
                return;
            }
        }

        var isNew = mode != ProductEditorMode.Edit;
        var title = mode switch
        {
            ProductEditorMode.Edit => "Edit product",
            ProductEditorMode.Duplicate => "Duplicate product",
            _ => "Add product",
        };
        var existingImageFull = mode == ProductEditorMode.Edit ? _images.GetFullPath(existing?.ImagePath) : null;

        var context = new ProductEditorContext(title, isNew, existing, options, prefillBarcode, existingImageFull);
        var result = _dialogs.ShowProductEditor(context);
        if (result is null)
        {
            return;
        }

        // Resolve an image selection into a managed file if needed.
        string? imagePath = mode == ProductEditorMode.Edit ? existing?.ImagePath : null;
        string? imageHash = mode == ProductEditorMode.Edit ? existing?.ImageHash : null;
        string? imageName = mode == ProductEditorMode.Edit ? existing?.ImageOriginalName : null;

        if (mode == ProductEditorMode.Duplicate && existing is not null)
        {
            imagePath = existing.ImagePath;
            imageHash = existing.ImageHash;
            imageName = existing.ImageOriginalName;
        }

        if (result.ImageAction == ProductImageAction.Replace && result.NewImageSourcePath is not null)
        {
            var saved = _images.Save(result.NewImageSourcePath);
            if (saved.Failed || saved.Value is null)
            {
                Feedback.Error(saved.ErrorMessage);
                return;
            }

            imagePath = saved.Value.RelativePath;
            imageHash = saved.Value.Hash;
            imageName = saved.Value.OriginalName;
        }
        else if (result.ImageAction == ProductImageAction.Remove)
        {
            imagePath = imageHash = imageName = null;
        }

        if (mode == ProductEditorMode.Edit && existing is not null)
        {
            var update = new UpdateProductRequest(
                existing.Id, result.Name, result.Sku, result.Barcode, result.CategoryId,
                result.Brand, result.Variant, result.Size, result.Unit, result.Cost, result.Price,
                result.TrackInventory, result.ReorderLevel, result.AllowNegativeStock, result.Notes,
                imagePath, imageHash, imageName);
            Apply(_products.Update(update, UserId), $"{result.Name} was saved.");
        }
        else
        {
            var create = new CreateProductRequest(
                result.Name, result.Sku, result.Barcode, result.CategoryId, result.Brand, result.Variant,
                result.Size, result.Unit, result.Cost, result.Price, result.TrackInventory, result.ReorderLevel,
                result.OpeningQuantity, result.AllowNegativeStock, IsActive: true, Notes: result.Notes,
                ImagePath: imagePath, ImageHash: imageHash, ImageOriginalName: imageName);
            var created = _products.Create(create, UserId, null);
            Apply(created, $"{result.Name} was created.");
        }
    }

    private void Apply(SnookerPoint.Application.Common.OperationResult result, string success)
    {
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Refresh();
        Feedback.Success(success);
    }

    private void LoadCategoryFilters()
    {
        var current = SelectedCategoryFilter?.Id;
        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryFilterOption(null, "All categories"));
        foreach (var c in _categories.GetAll())
        {
            CategoryFilters.Add(new CategoryFilterOption(c.Id, c.IsActive ? c.Name : $"{c.Name} (inactive)"));
        }

        SelectedCategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == current) ?? CategoryFilters[0];
    }

    private bool Has(Permission permission) =>
        _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, permission);
}

/// <summary>A category choice for the products filter ("All categories" has a null id).</summary>
public sealed record CategoryFilterOption(int? Id, string Name);

/// <summary>An active/inactive filter choice.</summary>
public sealed record ActiveFilterOption(ProductActiveFilter Value, string Label);
