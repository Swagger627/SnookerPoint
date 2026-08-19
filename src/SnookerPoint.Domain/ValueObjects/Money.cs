using System.Globalization;

namespace SnookerPoint.Domain.ValueObjects;

/// <summary>
/// An immutable money value stored as an integer number of minor units (paisa).
/// 100 paisa = Rs 1. Money is NEVER represented or persisted as a floating-point
/// number anywhere in the system — all arithmetic is exact integer arithmetic.
/// </summary>
/// <remarks>
/// The default currency for the initial market is the Pakistani Rupee (PKR),
/// displayed with the symbol "Rs". <see cref="Money"/> itself only tracks the
/// integer minor units; currency/symbol are a presentation concern.
/// </remarks>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>Number of minor units in one major unit (100 paisa = Rs 1).</summary>
    public const long MinorUnitsPerUnit = 100;

    /// <summary>The default display symbol for the initial market.</summary>
    public const string DefaultSymbol = "Rs";

    private readonly long _paisa;

    private Money(long paisa) => _paisa = paisa;

    /// <summary>A zero amount.</summary>
    public static Money Zero => new(0);

    /// <summary>The raw amount in minor units (paisa). This is what gets persisted.</summary>
    public long Paisa => _paisa;

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => _paisa == 0;

    /// <summary>True when the amount is below zero.</summary>
    public bool IsNegative => _paisa < 0;

    /// <summary>True when the amount is above zero.</summary>
    public bool IsPositive => _paisa > 0;

    /// <summary>Creates a money value directly from an integer number of paisa.</summary>
    public static Money FromPaisa(long paisa) => new(paisa);

    /// <summary>Creates a money value from a whole number of rupees.</summary>
    public static Money FromRupees(long rupees) => new(checked(rupees * MinorUnitsPerUnit));

    /// <summary>
    /// Creates a money value from a decimal rupee amount, rounding to the nearest
    /// paisa using banker's-free arithmetic (away-from-zero at the half).
    /// Use only at input boundaries; internal maths stays in paisa.
    /// </summary>
    public static Money FromRupees(decimal rupees)
    {
        var paisa = decimal.Round(rupees * MinorUnitsPerUnit, 0, MidpointRounding.AwayFromZero);
        return new Money((long)paisa);
    }

    /// <summary>The amount expressed in major units (rupees) for display only.</summary>
    public decimal ToRupees() => (decimal)_paisa / MinorUnitsPerUnit;

    public Money Add(Money other) => new(checked(_paisa + other._paisa));

    public Money Subtract(Money other) => new(checked(_paisa - other._paisa));

    /// <summary>Multiplies the amount by a whole quantity (e.g. line total = price × qty).</summary>
    public Money Multiply(long quantity) => new(checked(_paisa * quantity));

    public Money Negate() => new(checked(-_paisa));

    public Money Abs() => new(Math.Abs(_paisa));

    public static Money operator +(Money a, Money b) => a.Add(b);
    public static Money operator -(Money a, Money b) => a.Subtract(b);
    public static Money operator -(Money a) => a.Negate();
    public static Money operator *(Money a, long quantity) => a.Multiply(quantity);
    public static Money operator *(long quantity, Money a) => a.Multiply(quantity);

    public static bool operator ==(Money a, Money b) => a._paisa == b._paisa;
    public static bool operator !=(Money a, Money b) => a._paisa != b._paisa;
    public static bool operator <(Money a, Money b) => a._paisa < b._paisa;
    public static bool operator >(Money a, Money b) => a._paisa > b._paisa;
    public static bool operator <=(Money a, Money b) => a._paisa <= b._paisa;
    public static bool operator >=(Money a, Money b) => a._paisa >= b._paisa;

    public int CompareTo(Money other) => _paisa.CompareTo(other._paisa);

    public bool Equals(Money other) => _paisa == other._paisa;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => _paisa.GetHashCode();

    /// <summary>
    /// Formats the amount with the given symbol, e.g. "Rs 1,250" or "Rs 1,250.50".
    /// Whole-rupee amounts show no decimals; fractional amounts show two.
    /// </summary>
    public string Format(string symbol = DefaultSymbol)
    {
        var rupees = ToRupees();
        var body = _paisa % MinorUnitsPerUnit == 0
            ? rupees.ToString("#,##0", CultureInfo.InvariantCulture)
            : rupees.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return $"{symbol} {body}";
    }

    public override string ToString() => Format();
}
