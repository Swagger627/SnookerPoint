using SnookerPoint.Application.Catalog;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class CategoryServiceTests
{
    private static int CreateCashier(Phase1Environment env)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public void Create_AddsCategory()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.Categories.Create("Drinks", ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(env.Categories.GetAll(), c => c.Name == "Drinks" && c.IsActive);
    }

    [Fact]
    public void ActiveName_MustBeUnique_CaseInsensitive()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.Categories.Create("Drinks", ownerId);

        Assert.True(env.Categories.Create("drinks", ownerId).Failed);
    }

    [Fact]
    public void Deactivation_PreservesProducts()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Snacks");
        var productId = env.Products.Create(new CreateProductRequest(
            "Chips", "SNK-CHIP", "C1", cat, null, "Salted", "25 g", ProductUnit.Pack,
            null, Money.FromRupees(30), true, 5m), ownerId, shiftId).Value;

        Assert.True(env.Categories.SetActive(cat, false, ownerId).Succeeded);

        // The category is inactive but its product still exists and keeps the category.
        var product = env.Products.Get(productId)!;
        Assert.Equal(cat, product.CategoryId);
        Assert.Contains(env.Categories.GetAll(), c => c.Id == cat && !c.IsActive && c.ProductCount == 1);
    }

    [Fact]
    public void DeactivatedName_CanBeReused_ByANewActiveCategory()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Old");
        Assert.True(env.Categories.SetActive(cat, false, ownerId).Succeeded);

        // With the old one inactive, the name is free again.
        Assert.True(env.Categories.Create("Old", ownerId).Succeeded);
    }

    [Fact]
    public void Rename_IsRejected_WhenClashingWithAnotherActive()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.Categories.Create("Drinks", ownerId);
        var snacks = env.SeedCategory(ownerId, "Snacks");

        Assert.True(env.Categories.Update(snacks, "Drinks", 1, ownerId).Failed);
    }

    [Fact]
    public void UnauthorisedUser_CannotManageCategories()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var cashier = CreateCashier(env);

        Assert.True(env.Categories.Create("Nope", cashier).Failed);
    }
}
