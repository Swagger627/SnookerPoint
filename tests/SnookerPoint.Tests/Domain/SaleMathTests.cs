using SnookerPoint.Application.Sales;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Sales;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Tests.Domain;

public class SaleMathTests
{
    [Fact]
    public void LineTotal_MultipliesUnitPriceByQuantity()
    {
        Assert.Equal(18_000, SaleMath.LineTotal(Money.FromRupees(60), 3).Paisa);
        Assert.Equal(9_000, SaleMath.LineTotal(Money.FromRupees(60), 1.5m).Paisa);
    }

    [Fact]
    public void Compute_AddsTableChargeAndFloorsAtZero()
    {
        var totals = SaleMath.Compute(Money.FromRupees(100), Money.FromRupees(50), DiscountKind.None, 0, Money.Zero, Money.Zero);
        Assert.Equal(Money.FromRupees(150).Paisa, totals.Total.Paisa);
    }

    [Fact]
    public void FixedDiscount_ReducesTotal()
    {
        var totals = SaleMath.Compute(Money.FromRupees(200), Money.Zero, DiscountKind.FixedAmount, Money.FromRupees(30).Paisa, Money.Zero, Money.Zero);
        Assert.Equal(Money.FromRupees(30).Paisa, totals.Discount.Paisa);
        Assert.Equal(Money.FromRupees(170).Paisa, totals.Total.Paisa);
    }

    [Fact]
    public void PercentageDiscount_ReducesTotal()
    {
        var totals = SaleMath.Compute(Money.FromRupees(200), Money.Zero, DiscountKind.Percentage, 10, Money.Zero, Money.Zero);
        Assert.Equal(Money.FromRupees(20).Paisa, totals.Discount.Paisa);
        Assert.Equal(Money.FromRupees(180).Paisa, totals.Total.Paisa);
    }

    [Fact]
    public void Discount_CannotReduceBelowZero()
    {
        var totals = SaleMath.Compute(Money.FromRupees(50), Money.Zero, DiscountKind.FixedAmount, Money.FromRupees(999).Paisa, Money.Zero, Money.Zero);
        Assert.Equal(Money.FromRupees(50).Paisa, totals.Discount.Paisa); // clamped to base
        Assert.Equal(0, totals.Total.Paisa);
    }
}

public class PaymentMathTests
{
    private static PaymentEntry Cash(long rupees, long? received = null) =>
        new(PaymentMethodKind.Cash, Money.FromRupees(rupees), received is { } r ? Money.FromRupees(r) : null);

    private static PaymentEntry Electronic(long rupees) =>
        new(PaymentMethodKind.Electronic, Money.FromRupees(rupees), null);

    [Fact]
    public void ExactCash_IsValid_NoChange()
    {
        var v = PaymentMath.Validate(Money.FromRupees(850), new[] { Cash(850) });
        Assert.True(v.IsValid);
        Assert.Equal(0, v.Change.Paisa);
        Assert.Equal(Money.FromRupees(850).Paisa, v.CashApplied.Paisa);
    }

    [Fact]
    public void CashOverpayment_ProducesChange()
    {
        var v = PaymentMath.Validate(Money.FromRupees(850), new[] { Cash(850, received: 1000) });
        Assert.True(v.IsValid);
        Assert.Equal(Money.FromRupees(150).Paisa, v.Change.Paisa); // 1000 received − 850 applied
        Assert.Equal(Money.FromRupees(850).Paisa, v.CashApplied.Paisa);
    }

    [Fact]
    public void Underpayment_IsRejected()
    {
        var v = PaymentMath.Validate(Money.FromRupees(850), new[] { Cash(500) });
        Assert.False(v.IsValid);
        Assert.Equal(Money.FromRupees(350).Paisa, v.Remaining.Paisa);
    }

    [Fact]
    public void SplitCashAndElectronic_IsValid()
    {
        var v = PaymentMath.Validate(Money.FromRupees(1000), new[] { Electronic(700), Cash(300) });
        Assert.True(v.IsValid);
        Assert.Equal(Money.FromRupees(300).Paisa, v.CashApplied.Paisa);
        Assert.Equal(Money.FromRupees(700).Paisa, v.ElectronicApplied.Paisa);
    }

    [Fact]
    public void AppliedOverAmountDue_IsRejected()
    {
        var v = PaymentMath.Validate(Money.FromRupees(500), new[] { Electronic(400), Cash(300) });
        Assert.False(v.IsValid);
    }

    [Fact]
    public void ElectronicOnly_CannotExceedDue()
    {
        var v = PaymentMath.Validate(Money.FromRupees(500), new[] { Electronic(600) });
        Assert.False(v.IsValid);
    }
}

public class ReceiptRendererTests
{
    private static ReceiptData Sample() => new(
        "Snooker Point", "123 Main St", "0300 0000000", 42, DateTimeOffset.UtcNow, "The Owner",
        "Walk-in", null,
        new[] { new ReceiptLine("Cola 330", 2, Money.FromRupees(60), Money.FromRupees(120)) },
        Money.Zero, Money.FromRupees(120), Money.Zero, Money.Zero, Money.Zero, Money.FromRupees(120),
        new[] { new ReceiptPayment("Cash", Money.FromRupees(120), Money.FromRupees(200), Money.FromRupees(80), null) },
        Money.FromRupees(200), Money.FromRupees(80));

    [Fact]
    public void Render58_And80_ProduceContent()
    {
        var narrow = ReceiptRenderer.Render(Sample(), 58, isReprint: false);
        var wide = ReceiptRenderer.Render(Sample(), 80, isReprint: false);

        Assert.Contains("Snooker Point", narrow);
        Assert.Contains("Receipt #42", narrow);
        Assert.Contains("Cola 330", narrow);
        Assert.Contains("Change", narrow);
        Assert.DoesNotContain("REPRINT", narrow);
        Assert.Equal(32, ReceiptRenderer.ColumnsFor(58));
        Assert.Equal(48, ReceiptRenderer.ColumnsFor(80));
        Assert.Contains("Snooker Point", wide);
    }

    [Fact]
    public void Reprint_IsClearlyMarked()
    {
        var text = ReceiptRenderer.Render(Sample(), 58, isReprint: true);
        Assert.Contains("REPRINT", text);
    }
}
