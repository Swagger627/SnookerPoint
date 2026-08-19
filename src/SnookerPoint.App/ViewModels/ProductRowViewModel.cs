using SnookerPoint.Application.Catalog;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>A read-only product row for the catalogue list. Resolves its image safely.</summary>
public sealed class ProductRowViewModel
{
    private readonly ProductListItem _item;

    public ProductRowViewModel(ProductListItem item, string? imageFullPath)
    {
        _item = item;
        ImageFullPath = imageFullPath;
    }

    public int Id => _item.Id;
    public string Name => _item.Name;
    public string Sku => _item.Sku;
    public string Barcode => _item.Barcode ?? "—";
    public string CategoryName => _item.CategoryName;
    public string Variant => _item.Variant ?? string.Empty;
    public string Size => _item.Size ?? string.Empty;
    public string PriceText => _item.Price.Format();
    public string CostText => _item.Cost?.Format() ?? "—";
    public bool IsActive => _item.IsActive;
    public string ActiveToggleText => IsActive ? "Deactivate" : "Activate";

    public string StockText => _item.TrackInventory
        ? _item.CurrentStock.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)
        : "—";

    public string StatusText => StatusLabels.For(_item.Status);

    public string? ImageFullPath { get; }
    public bool HasImage => !string.IsNullOrWhiteSpace(ImageFullPath);
}

/// <summary>Friendly, written labels for stock statuses (colour is never the only signal).</summary>
public static class StatusLabels
{
    public static string For(StockStatus status) => status switch
    {
        StockStatus.InStock => "In stock",
        StockStatus.LowStock => "Low stock",
        StockStatus.OutOfStock => "Out of stock",
        StockStatus.NotTracked => "Not tracked",
        StockStatus.Inactive => "Inactive",
        _ => status.ToString(),
    };
}
