namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A product category (e.g. Drinks, Snacks, Accessories). Categories are never
/// hard-deleted once products reference them — they are deactivated instead so that
/// historical product records keep a valid category.
/// </summary>
public sealed class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
