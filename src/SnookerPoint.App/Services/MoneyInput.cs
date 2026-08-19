using System.Globalization;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

/// <summary>Parses user-entered rupee amounts into <see cref="Money"/>.</summary>
public static class MoneyInput
{
    /// <summary>
    /// Parses a non-negative rupee amount. Returns false for blank, non-numeric or
    /// negative input. Callers decide whether zero is acceptable.
    /// </summary>
    public static bool TryParseRupees(string? text, out Money money)
    {
        money = Money.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var rupees))
        {
            return false;
        }

        if (rupees < 0)
        {
            return false;
        }

        money = Money.FromRupees(rupees);
        return true;
    }
}
