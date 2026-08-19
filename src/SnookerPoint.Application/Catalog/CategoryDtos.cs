namespace SnookerPoint.Application.Catalog;

/// <summary>A category row for the management screen, with how many products use it.</summary>
public sealed record CategoryListItem(
    int Id,
    string Name,
    int SortOrder,
    bool IsActive,
    int ProductCount);
