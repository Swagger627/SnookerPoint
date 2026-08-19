using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Tests.Domain;

public class MoneyTests
{
    [Fact]
    public void Zero_HasNoPaisa()
    {
        Assert.Equal(0, Money.Zero.Paisa);
        Assert.True(Money.Zero.IsZero);
    }

    [Fact]
    public void FromRupees_Whole_ConvertsToPaisa()
    {
        var m = Money.FromRupees(800L);
        Assert.Equal(80_000, m.Paisa);
        Assert.Equal(800m, m.ToRupees());
    }

    [Theory]
    [InlineData(0.01, 1)]
    [InlineData(1.5, 150)]
    [InlineData(1250.50, 125050)]
    [InlineData(0.005, 1)]   // rounds away from zero at the half
    public void FromRupees_Decimal_RoundsToNearestPaisa(double rupees, long expectedPaisa)
    {
        var m = Money.FromRupees((decimal)rupees);
        Assert.Equal(expectedPaisa, m.Paisa);
    }

    [Fact]
    public void Add_And_Subtract_AreExact()
    {
        var a = Money.FromPaisa(125_050);
        var b = Money.FromPaisa(74_950);
        Assert.Equal(200_000, (a + b).Paisa);
        Assert.Equal(50_100, (a - b).Paisa);
    }

    [Fact]
    public void Multiply_ByQuantity_ScalesPaisa()
    {
        var price = Money.FromPaisa(15_000); // Rs 150
        Assert.Equal(45_000, (price * 3).Paisa);
        Assert.Equal(45_000, (3 * price).Paisa);
    }

    [Fact]
    public void Negate_FlipsSign()
    {
        var m = Money.FromPaisa(500);
        Assert.Equal(-500, (-m).Paisa);
        Assert.True((-m).IsNegative);
        Assert.Equal(500, (-m).Abs().Paisa);
    }

    [Fact]
    public void Comparison_Operators_Work()
    {
        var small = Money.FromPaisa(100);
        var large = Money.FromPaisa(200);
        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= Money.FromPaisa(100));
        Assert.True(small >= Money.FromPaisa(100));
        Assert.True(small != large);
        Assert.True(small == Money.FromPaisa(100));
    }

    [Fact]
    public void Equality_And_HashCode_MatchOnPaisa()
    {
        var a = Money.FromPaisa(999);
        var b = Money.FromPaisa(999);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Format_WholeRupees_HasNoDecimals()
    {
        Assert.Equal("Rs 1,250", Money.FromPaisa(125_000).Format());
    }

    [Fact]
    public void Format_FractionalRupees_ShowsTwoDecimals()
    {
        Assert.Equal("Rs 1,250.50", Money.FromPaisa(125_050).Format());
    }

    [Fact]
    public void Multiply_Overflow_Throws()
    {
        var huge = Money.FromPaisa(long.MaxValue);
        Assert.Throws<OverflowException>(() => huge * 2);
    }
}
