using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Catalog;

/// <summary>
/// Manages the product catalogue. Enforces unique SKU/barcode, non-negative money, a
/// required name and price, and treats each flavour/size/barcode as a separate SKU.
/// Creating a product with an opening quantity also writes an Opening Stock movement in
/// the same transaction. Every change is audited.
/// </summary>
public interface IProductService
{
    IReadOnlyList<ProductListItem> GetList(ProductFilter filter);

    ProductDetail? Get(int id);

    /// <summary>Finds a product by exact barcode (for scanner lookup). Null if not found.</summary>
    ProductListItem? FindByBarcode(string barcode);

    OperationResult<int> Create(CreateProductRequest request, int actorUserId, int? shiftId);

    OperationResult Update(UpdateProductRequest request, int actorUserId);

    /// <summary>Copies a product into a new SKU/barcode (no stock is copied).</summary>
    OperationResult<int> Duplicate(int sourceId, string newSku, string? newBarcode, int actorUserId);

    OperationResult SetActive(int id, bool active, int actorUserId);
}
