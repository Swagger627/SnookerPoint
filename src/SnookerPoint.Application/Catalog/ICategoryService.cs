using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Catalog;

/// <summary>
/// Manages product categories. Active names are unique (case-insensitive), a category
/// that has products is deactivated rather than deleted, and every change is audited.
/// </summary>
public interface ICategoryService
{
    IReadOnlyList<CategoryListItem> GetAll(bool includeInactive = true);

    OperationResult<int> Create(string name, int actorUserId);

    /// <summary>Renames and/or reorders a category.</summary>
    OperationResult Update(int id, string name, int sortOrder, int actorUserId);

    OperationResult SetActive(int id, bool active, int actorUserId);
}
