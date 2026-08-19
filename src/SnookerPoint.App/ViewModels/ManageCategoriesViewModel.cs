using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Category management: add, rename, reorder, activate/deactivate. Active names are unique;
/// a category with products is deactivated (not deleted) and keeps its products.
/// </summary>
public partial class ManageCategoriesViewModel : ObservableObject
{
    private readonly ICategoryService _categories;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IThemeService _theme;
    private readonly INavigationService _navigation;

    public ManageCategoriesViewModel(
        ICategoryService categories,
        ISessionContext session,
        IPermissionService permissions,
        IThemeService theme,
        INavigationService navigation)
    {
        _categories = categories;
        _session = session;
        _permissions = permissions;
        _theme = theme;
        _navigation = navigation;

        Reload();
    }

    public FeedbackViewModel Feedback { get; } = new();
    public ObservableCollection<CategoryRowViewModel> Rows { get; } = new();

    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;
    public string RoleName => _session.CurrentUser?.Role.ToString() ?? string.Empty;
    public bool CanManage => _session.CurrentUser is { } u && _permissions.HasPermission(u.Role, Permission.ManageProducts);
    public bool IsEmpty => Rows.Count == 0;

    [ObservableProperty] private string _newCategoryName = string.Empty;

    private int UserId => _session.CurrentUser!.Id;

    [RelayCommand]
    private void Add()
    {
        if (!CanManage)
        {
            return;
        }

        Feedback.Clear();
        var result = _categories.Create(NewCategoryName, UserId);
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        NewCategoryName = string.Empty;
        Reload();
        Feedback.Success("Category added.");
    }

    [RelayCommand]
    private void Save(CategoryRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var result = _categories.Update(row.Id, row.Name, row.SortOrder, UserId);
        Report(result, $"'{row.Name}' saved.");
    }

    [RelayCommand]
    private void ToggleActive(CategoryRowViewModel? row)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        Feedback.Clear();
        var result = _categories.SetActive(row.Id, !row.IsActive, UserId);
        Report(result, row.IsActive ? $"'{row.Name}' deactivated." : $"'{row.Name}' activated.");
    }

    [RelayCommand]
    private void MoveUp(CategoryRowViewModel? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(CategoryRowViewModel? row) => Move(row, +1);

    private void Move(CategoryRowViewModel? row, int direction)
    {
        if (row is null || !CanManage)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Feedback.Clear();
        var other = Rows[target];
        // Swap their sort orders and persist both.
        (row.SortOrder, other.SortOrder) = (other.SortOrder, row.SortOrder);
        var a = _categories.Update(row.Id, row.Name, row.SortOrder, UserId);
        var b = _categories.Update(other.Id, other.Name, other.SortOrder, UserId);
        if (a.Failed || b.Failed)
        {
            Feedback.Error(a.Failed ? a.ErrorMessage : b.ErrorMessage);
        }

        Reload();
    }

    [RelayCommand]
    private void OpenProducts() => _navigation.ShowProducts();

    [RelayCommand]
    private void Home() => _navigation.ShowHome();

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    private void Report(SnookerPoint.Application.Common.OperationResult result, string success)
    {
        if (result.Failed)
        {
            Feedback.Error(result.ErrorMessage);
            return;
        }

        Reload();
        Feedback.Success(success);
    }

    private void Reload()
    {
        Rows.Clear();
        foreach (var c in _categories.GetAll())
        {
            Rows.Add(new CategoryRowViewModel(c));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>An editable category row.</summary>
public partial class CategoryRowViewModel : ObservableObject
{
    public CategoryRowViewModel(CategoryListItem item)
    {
        Id = item.Id;
        _name = item.Name;
        SortOrder = item.SortOrder;
        IsActive = item.IsActive;
        ProductCount = item.ProductCount;
    }

    public int Id { get; }
    public int SortOrder { get; set; }
    public bool IsActive { get; }
    public int ProductCount { get; }

    [ObservableProperty] private string _name;

    public string ActiveToggleText => IsActive ? "Deactivate" : "Activate";
    public string StatusText => IsActive ? "Active" : "Inactive";
    public string ProductCountText => ProductCount == 1 ? "1 product" : $"{ProductCount} products";
}
