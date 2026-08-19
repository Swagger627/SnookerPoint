using System.Linq;
using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>Covers the New Sale POS view model headlessly: cart, barcode, payment, permissions.</summary>
public class NewSaleViewModelTests
{
    private static (NewSaleViewModel Vm, FakeDialogService Dialogs, Phase1Environment Env, int OwnerId, int ShiftId, int ProductId)
        Create(Phase1Environment env, UserRole role = UserRole.Owner)
    {
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees: 60, opening: 10m, barcode: "0012345");

        var actorId = ownerId;
        if (role != UserRole.Owner)
        {
            using var db = env.NewContext();
            var user = new SnookerPoint.Domain.Entities.User { DisplayName = role.ToString(), Username = role + "-u", Role = role, PasswordHash = "x", IsActive = true };
            db.Users.Add(user);
            db.SaveChanges();
            actorId = user.Id;
        }

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(actorId, role.ToString(), role.ToString().ToLower(), role, HasPin: false));
        var dialogs = new FakeDialogService();
        var vm = new NewSaleViewModel(env.Sales, env.Products, env.PaymentMethods, env.SalesQuery, env.Shifts,
            session, new PermissionService(), dialogs, new FakeNavigationService(), new FakeThemeService());
        return (vm, dialogs, env, ownerId, shiftId, productId);
    }

    [Fact]
    public void AddProduct_AddsToCart()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _, _) = Create(env);
        var row = vm.Products.First(p => p.Sku == "P1");

        vm.AddProductCommand.Execute(row);

        Assert.Single(vm.Cart);
        Assert.Equal("Rs 60", vm.TotalText);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Scan_KnownBarcode_AddsAndMergesQuantity()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _, _) = Create(env);

        vm.SearchText = "0012345";
        vm.ScanCommand.Execute(null);
        vm.SearchText = "0012345";
        vm.ScanCommand.Execute(null);

        var line = Assert.Single(vm.Cart);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public void Scan_UnknownBarcode_ShowsWarning()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _, _) = Create(env);

        vm.SearchText = "999999";
        vm.ScanCommand.Execute(null);

        Assert.Equal(FeedbackKind.Warning, vm.Feedback.Kind);
        Assert.Empty(vm.Cart);
    }

    [Fact]
    public void PayNow_CompletesSale_ShowsReceipt_AndFeedback()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, envRef, ownerId, shiftId, productId) = Create(env);
        vm.AddProductCommand.Execute(vm.Products.First(p => p.Sku == "P1")); // total 60

        dialogs.PaymentResult = new PaymentDialogResult(
            new[] { new PaymentInput(envRef.CashMethodId, Money.FromRupees(60), Money.FromRupees(60), null, null) },
            Money.Zero);
        dialogs.ReceiptPreviewPrints = true;

        vm.PayNowCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.True(dialogs.ReceiptPreviewShown);
        Assert.Single(envRef.SalesQuery.GetHistory(new SalesHistoryFilter()));
        Assert.Equal(9m, envRef.Inventory.GetCurrentStock(productId)); // 10 − 1, deducted once
        Assert.True(vm.IsEmpty); // a fresh draft started
    }

    [Fact]
    public void PayNow_WithoutOpenShift_ShowsError_DoesNotComplete()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, envRef, ownerId, shiftId, _) = Create(env);
        vm.AddProductCommand.Execute(vm.Products.First(p => p.Sku == "P1"));
        envRef.Shifts.CloseShift(shiftId, Money.Zero, null);

        vm.PayNowCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
        Assert.Contains("shift", vm.Feedback.Message);
        Assert.Empty(envRef.SalesQuery.GetHistory(new SalesHistoryFilter()));
    }

    [Fact]
    public void FloorStaff_CannotCompletePayment()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _, _, _) = Create(env, UserRole.FloorStaff);
        Assert.False(vm.CanCompletePayment);

        vm.AddProductCommand.Execute(vm.Products.First(p => p.Sku == "P1"));
        vm.PayNowCommand.Execute(null);

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
    }

    [Fact]
    public void Hold_ParksSale_AndStartsFresh()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _, _) = Create(env);
        vm.AddProductCommand.Execute(vm.Products.First(p => p.Sku == "P1"));

        vm.HoldCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains(vm.Held, h => true);
        Assert.True(vm.IsEmpty);
    }
}
