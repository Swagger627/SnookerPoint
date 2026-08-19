using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A configurable payment method (Cash, EasyPaisa, JazzCash, Bank Transfer, …). Methods
/// used in history are never hard-deleted — they are deactivated instead.
/// </summary>
public sealed class PaymentMethod
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public PaymentMethodKind Kind { get; set; } = PaymentMethodKind.Electronic;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>System methods (e.g. Cash) cannot be deactivated.</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
