using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Sales;

/// <summary>
/// Manages configurable payment methods. Methods used in history are never deleted, only
/// deactivated; the system Cash method cannot be deactivated. Seeded on first use with
/// Cash, EasyPaisa, JazzCash and Bank Transfer.
/// </summary>
public interface IPaymentMethodService
{
    IReadOnlyList<PaymentMethodOption> GetAll(bool includeInactive = true);

    /// <summary>The active methods offered at payment time.</summary>
    IReadOnlyList<PaymentMethodOption> GetActive();

    OperationResult SetActive(int id, bool active, int actorUserId);
}
