using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Settings;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>Reads and updates the global billing settings, with validation, permission and audit.</summary>
public sealed class BillingSettingsService : IBillingSettingsService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<BillingSettingsService> _logger;

    public BillingSettingsService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<BillingSettingsService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public BillingSettingsView Get()
    {
        using var db = _factory.CreateDbContext();
        var settings = db.BillingSettings.AsNoTracking().FirstOrDefault(s => s.Id == 1);
        if (settings is null)
        {
            var d = BillingPolicy.Default;
            return new BillingSettingsView(d.Method, d.RoundingIncrementMinutes, d.MinimumBillableMinutes, d.GracePeriodMinutes);
        }

        return new BillingSettingsView(
            settings.Method,
            settings.RoundingIncrementMinutes,
            settings.MinimumBillableMinutes,
            settings.GracePeriodMinutes);
    }

    public OperationResult Update(
        BillingMethod method,
        int roundingIncrementMinutes,
        int minimumBillableMinutes,
        int gracePeriodMinutes,
        int actorUserId)
    {
        var errors = BillingPolicy.Validate(method, roundingIncrementMinutes, minimumBillableMinutes, gracePeriodMinutes);
        if (errors.Count > 0)
        {
            return OperationResult.Failure(errors);
        }

        using var db = _factory.CreateDbContext();

        var user = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (user is null || !_permissions.HasPermission(user, Permission.ManageBillingSettings))
        {
            return OperationResult.Failure("You do not have permission to change billing settings.");
        }

        var now = _clock.UtcNow;
        var settings = db.BillingSettings.FirstOrDefault(s => s.Id == 1);
        if (settings is null)
        {
            settings = new BillingSettings { Id = 1 };
            db.BillingSettings.Add(settings);
        }

        settings.Method = method;
        settings.RoundingIncrementMinutes = roundingIncrementMinutes;
        settings.MinimumBillableMinutes = minimumBillableMinutes;
        settings.GracePeriodMinutes = gracePeriodMinutes;
        settings.UpdatedUtc = now;

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = now,
            Action = AuditActions.BillingSettingsUpdated,
            ActorUserId = actorUserId,
            Entity = nameof(BillingSettings),
            EntityId = "1",
            Details = new BillingPolicy(method, roundingIncrementMinutes, minimumBillableMinutes, gracePeriodMinutes).Summary(),
        });
        db.SaveChanges();

        _logger.LogInformation("Billing settings updated by user {UserId}.", actorUserId);
        return OperationResult.Success();
    }
}
