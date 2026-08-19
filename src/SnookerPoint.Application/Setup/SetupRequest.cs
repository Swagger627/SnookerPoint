using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Setup;

/// <summary>One table row captured in the setup wizard's Table Setup step.</summary>
public sealed record SetupTableInput(
    string Name,
    TableType Type,
    Money HourlyRate,
    bool IsActive);

/// <summary>The owner account captured in the setup wizard's Owner Account step.</summary>
public sealed record OwnerAccountInput(
    string DisplayName,
    string Username,
    string Password,
    string? Pin);

/// <summary>
/// The complete first-run setup payload. Persisted atomically by
/// <see cref="ISetupService.CompleteSetup"/>.
/// </summary>
public sealed record SetupRequest(
    string ClubName,
    string? Address,
    string? Phone,
    string Theme,
    string Language,
    int ReceiptWidthMm,
    string? PrinterName,
    bool AutoPrintReceipt,
    string? BackupFolder,
    IReadOnlyList<SetupTableInput> Tables,
    OwnerAccountInput Owner);
