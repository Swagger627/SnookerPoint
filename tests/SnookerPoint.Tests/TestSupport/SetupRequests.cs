using SnookerPoint.Application.Setup;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>Builders for setup payloads used across tests.</summary>
public static class SetupRequests
{
    public static List<SetupTableInput> DefaultTables(int count = 5, long ratePaisa = 80_000) =>
        Enumerable.Range(1, count)
            .Select(i => new SetupTableInput($"Table {i}", TableType.Snooker, Money.FromPaisa(ratePaisa), true))
            .ToList();

    public static SetupRequest Valid(
        IReadOnlyList<SetupTableInput>? tables = null,
        string username = "owner",
        string password = "secret123",
        string? pin = null,
        string clubName = "Test Club") =>
        new(
            ClubName: clubName,
            Address: "123 Main St",
            Phone: "0300 0000000",
            Theme: "Dark",
            Language: "en",
            ReceiptWidthMm: 58,
            PrinterName: null,
            AutoPrintReceipt: false,
            BackupFolder: null,
            Tables: tables ?? DefaultTables(),
            Owner: new OwnerAccountInput("The Owner", username, password, pin));
}
