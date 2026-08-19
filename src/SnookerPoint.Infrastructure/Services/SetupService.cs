using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Setup;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>Validates and persists first-run setup atomically.</summary>
public sealed class SetupService : ISetupService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ISecretHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<SetupService> _logger;

    public SetupService(
        IDbContextFactory<SnookerPointDbContext> factory,
        ISecretHasher hasher,
        IClock clock,
        ILogger<SetupService> logger)
    {
        _factory = factory;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public bool IsSetupComplete()
    {
        using var db = _factory.CreateDbContext();
        return db.ClubSettings.AsNoTracking().Any(c => c.IsSetupComplete);
    }

    public OperationResult CompleteSetup(SetupRequest request)
    {
        using var db = _factory.CreateDbContext();

        if (db.ClubSettings.AsNoTracking().Any(c => c.IsSetupComplete))
        {
            return OperationResult.Failure("Setup has already been completed on this computer.");
        }

        var username = (request.Owner.Username ?? string.Empty).Trim().ToLowerInvariant();
        var errors = Validate(request, username, db);
        if (errors.Count > 0)
        {
            return OperationResult.Failure(errors);
        }

        var now = _clock.UtcNow;

        using var transaction = db.Database.BeginTransaction();
        try
        {
            db.ClubSettings.Add(new ClubSettings
            {
                Id = 1,
                ClubName = request.ClubName.Trim(),
                Address = Clean(request.Address),
                Phone = Clean(request.Phone),
                CurrencyCode = "PKR",
                CurrencySymbol = "Rs",
                Theme = request.Theme,
                Language = request.Language,
                ReceiptWidthMm = request.ReceiptWidthMm,
                PrinterName = Clean(request.PrinterName),
                AutoPrintReceipt = request.AutoPrintReceipt,
                BackupFolder = Clean(request.BackupFolder),
                IsSetupComplete = true,
                SetupCompletedUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now,
            });

            var sortOrder = 0;
            foreach (var table in request.Tables)
            {
                db.PoolTables.Add(new PoolTable
                {
                    Name = table.Name.Trim(),
                    Type = table.Type,
                    HourlyRate = table.HourlyRate,
                    IsActive = table.IsActive,
                    SortOrder = sortOrder++,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                });
            }

            var owner = new User
            {
                DisplayName = request.Owner.DisplayName.Trim(),
                Username = username,
                Role = UserRole.Owner,
                PasswordHash = _hasher.Hash(request.Owner.Password),
                PinHash = string.IsNullOrEmpty(request.Owner.Pin) ? null : _hasher.Hash(request.Owner.Pin!),
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            db.Users.Add(owner);
            db.SaveChanges();

            db.AuditEvents.Add(new AuditEvent
            {
                Utc = now,
                Action = AuditActions.SetupCompleted,
                ActorUserId = owner.Id,
                Entity = nameof(ClubSettings),
                EntityId = "1",
                Details = $"Initial setup completed for '{request.ClubName.Trim()}' with {request.Tables.Count(t => t.IsActive)} active table(s).",
            });
            db.SaveChanges();

            transaction.Commit();
            _logger.LogInformation("First-run setup completed successfully.");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "First-run setup failed and was rolled back.");
            return OperationResult.Failure(
                "Something went wrong while saving your setup. No changes were saved. Please try again.");
        }
    }

    private static List<string> Validate(SetupRequest request, string normalizedUsername, SnookerPointDbContext db)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ClubName))
        {
            errors.Add("Please enter the club name.");
        }

        if (request.ReceiptWidthMm is not (58 or 80))
        {
            errors.Add("Receipt width must be 58 mm or 80 mm.");
        }

        // Tables
        var activeTables = request.Tables.Where(t => t.IsActive).ToList();
        if (activeTables.Count == 0)
        {
            errors.Add("Please keep at least one table active.");
        }

        if (activeTables.Any(t => string.IsNullOrWhiteSpace(t.Name)))
        {
            errors.Add("Every active table needs a name.");
        }

        var duplicateNames = activeTables
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Name.Trim())
            .ToList();
        if (duplicateNames.Count > 0)
        {
            errors.Add($"Table names must be different. Duplicate: {string.Join(", ", duplicateNames)}.");
        }

        if (request.Tables.Any(t => t.HourlyRate.IsNegative))
        {
            errors.Add("Table rates cannot be negative.");
        }

        // Owner
        if (string.IsNullOrWhiteSpace(request.Owner.DisplayName))
        {
            errors.Add("Please enter the owner's display name.");
        }

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            errors.Add("Please enter a username.");
        }
        else if (db.Users.AsNoTracking().Any(u => u.Username == normalizedUsername))
        {
            errors.Add("That username is already taken. Please choose another.");
        }

        if (string.IsNullOrEmpty(request.Owner.Password) || request.Owner.Password.Length < SetupRules.MinPasswordLength)
        {
            errors.Add($"The password must be at least {SetupRules.MinPasswordLength} characters.");
        }

        if (!string.IsNullOrEmpty(request.Owner.Pin))
        {
            var pin = request.Owner.Pin!;
            if (!pin.All(char.IsDigit))
            {
                errors.Add("The PIN must contain digits only.");
            }
            else if (pin.Length < SetupRules.MinPinLength || pin.Length > SetupRules.MaxPinLength)
            {
                errors.Add($"The PIN must be {SetupRules.MinPinLength} to {SetupRules.MaxPinLength} digits.");
            }
        }

        return errors;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
