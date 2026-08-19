using System.Globalization;
using System.Text;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Sales;

/// <summary>
/// Pure text renderer for thermal receipts at 58 mm or 80 mm width (32 or 48 monospace
/// columns). Produces the immutable receipt snapshot stored with the sale and shown in the
/// print preview. A reprint is clearly marked. No dependencies — trivially testable.
/// </summary>
public static class ReceiptRenderer
{
    public static int ColumnsFor(int widthMm) => widthMm >= 80 ? 48 : 32;

    public static string Render(ReceiptData data, int widthMm, bool isReprint)
    {
        var cols = ColumnsFor(widthMm);
        var sb = new StringBuilder();

        void Center(string text) => sb.AppendLine(CenterText(text, cols));
        void Line(string text = "") => sb.AppendLine(Truncate(text, cols));
        void Rule() => sb.AppendLine(new string('-', cols));
        void Row(string left, string right) => sb.AppendLine(TwoColumns(left, right, cols));

        Center(data.ClubName);
        if (!string.IsNullOrWhiteSpace(data.Address))
        {
            Center(data.Address!);
        }

        if (!string.IsNullOrWhiteSpace(data.Phone))
        {
            Center(data.Phone!);
        }

        if (isReprint)
        {
            Line();
            Center("*** REPRINT ***");
        }

        Rule();
        Row($"Receipt #{data.SaleNumber}", data.SaleTypeText);
        Line(data.CompletedUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture));
        Line($"Cashier: {data.CashierName}");
        if (!string.IsNullOrWhiteSpace(data.TableInfo))
        {
            Line(data.TableInfo!);
        }

        Rule();
        foreach (var item in data.Lines)
        {
            Line(item.Name);
            var qtyPrice = $"{FormatQty(item.Quantity)} x {item.UnitPrice.Format()}";
            Row("  " + qtyPrice, item.LineTotal.Format());
        }

        if (data.TableCharge.IsPositive)
        {
            Row("Table charge", data.TableCharge.Format());
        }

        Rule();
        Row("Subtotal", data.Subtotal.Format());
        if (data.TableCharge.IsPositive)
        {
            Row("Table charge", data.TableCharge.Format());
        }

        if (data.Discount.IsPositive)
        {
            Row("Discount", "-" + data.Discount.Format());
        }

        if (data.Tax.IsPositive)
        {
            Row("Tax", data.Tax.Format());
        }

        if (data.Service.IsPositive)
        {
            Row("Service", data.Service.Format());
        }

        Row("TOTAL", data.Total.Format());
        Rule();

        foreach (var payment in data.Payments)
        {
            Row(payment.MethodName, payment.Amount.Format());
            if (!string.IsNullOrWhiteSpace(payment.Reference))
            {
                Line($"  Ref: {payment.Reference}");
            }
        }

        if (data.CashReceived is { } received && received.IsPositive)
        {
            Row("Cash received", received.Format());
            Row("Change", (data.Change ?? Money.Zero).Format());
        }

        Rule();
        Center("Thank you!");
        Line();

        return sb.ToString();
    }

    private static string FormatQty(decimal qty) =>
        qty == decimal.Truncate(qty)
            ? qty.ToString("0", CultureInfo.InvariantCulture)
            : qty.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Truncate(string text, int cols) =>
        text.Length <= cols ? text : text.Substring(0, cols);

    private static string CenterText(string text, int cols)
    {
        text = Truncate(text, cols);
        var pad = (cols - text.Length) / 2;
        return new string(' ', Math.Max(0, pad)) + text;
    }

    private static string TwoColumns(string left, string right, int cols)
    {
        right ??= string.Empty;
        var maxLeft = Math.Max(0, cols - right.Length - 1);
        if (left.Length > maxLeft)
        {
            left = left.Substring(0, maxLeft);
        }

        var spaces = cols - left.Length - right.Length;
        if (spaces < 1)
        {
            spaces = 1;
        }

        return left + new string(' ', spaces) + right;
    }
}
