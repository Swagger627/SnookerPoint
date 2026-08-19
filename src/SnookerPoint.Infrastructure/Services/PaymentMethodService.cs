using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Manages payment methods. Seeds the defaults (Cash, EasyPaisa, JazzCash, Bank Transfer)
/// on first use. Cash is a system method and cannot be deactivated. Methods are never
/// hard-deleted. Requires <see cref="Permission.ManagePaymentMethods"/> to change.
/// </summary>
public sealed class PaymentMethodService : IPaymentMethodService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<PaymentMethodService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<PaymentMethodOption> GetAll(bool includeInactive = true)
    {
        using var db = _factory.CreateDbContext();
        EnsureSeeded(db);

        var query = db.PaymentMethods.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        return query
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new PaymentMethodOption(m.Id, m.Name, m.Kind, m.IsActive, m.IsSystem))
            .ToList();
    }

    public IReadOnlyList<PaymentMethodOption> GetActive() => GetAll(includeInactive: false);

    public OperationResult SetActive(int id, bool active, int actorUserId)
    {
        using var db = _factory.CreateDbContext();
        EnsureSeeded(db);

        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, Permission.ManagePaymentMethods))
        {
            return OperationResult.Failure("You do not have permission to manage payment methods.");
        }

        var method = db.PaymentMethods.FirstOrDefault(m => m.Id == id);
        if (method is null)
        {
            return OperationResult.Failure("That payment method was not found.");
        }

        if (method.IsSystem && !active)
        {
            return OperationResult.Failure($"{method.Name} cannot be deactivated.");
        }

        if (method.IsActive == active)
        {
            return OperationResult.Success();
        }

        method.IsActive = active;
        method.UpdatedUtc = _clock.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = AuditActions.PaymentMethodConfigured,
            ActorUserId = actorUserId,
            Entity = nameof(PaymentMethod),
            EntityId = method.Id.ToString(),
            Details = $"'{method.Name}' {(active ? "activated" : "deactivated")}.",
        });
        db.SaveChanges();
        return OperationResult.Success();
    }

    private void EnsureSeeded(SnookerPointDbContext db)
    {
        if (db.PaymentMethods.Any())
        {
            return;
        }

        var now = _clock.UtcNow;
        db.PaymentMethods.AddRange(
            new PaymentMethod { Name = "Cash", Kind = PaymentMethodKind.Cash, SortOrder = 0, IsActive = true, IsSystem = true, CreatedUtc = now, UpdatedUtc = now },
            new PaymentMethod { Name = "EasyPaisa", Kind = PaymentMethodKind.Electronic, SortOrder = 1, IsActive = true, CreatedUtc = now, UpdatedUtc = now },
            new PaymentMethod { Name = "JazzCash", Kind = PaymentMethodKind.Electronic, SortOrder = 2, IsActive = true, CreatedUtc = now, UpdatedUtc = now },
            new PaymentMethod { Name = "Bank Transfer", Kind = PaymentMethodKind.Electronic, SortOrder = 3, IsActive = true, CreatedUtc = now, UpdatedUtc = now });

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // A concurrent caller seeded first; ignore the unique-name clash.
        }
    }
}
