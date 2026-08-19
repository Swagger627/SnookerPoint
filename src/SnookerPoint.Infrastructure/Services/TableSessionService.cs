using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Billing;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Sessions;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Live table-management: start/pause/resume/transfer/finish, corrections, dashboard
/// and history. State changes require an open shift + permission, run in a
/// transaction, and are audited. All charge maths goes through the billing calculator.
/// </summary>
public sealed class TableSessionService : ITableSessionService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ISessionBillingCalculator _calculator;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<TableSessionService> _logger;

    public TableSessionService(
        IDbContextFactory<SnookerPointDbContext> factory,
        ISessionBillingCalculator calculator,
        IPermissionService permissions,
        IClock clock,
        ILogger<TableSessionService> logger)
    {
        _factory = factory;
        _calculator = calculator;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    // ==================== DASHBOARD ====================

    public IReadOnlyList<TableCard> GetDashboard()
    {
        using var db = _factory.CreateDbContext();

        var tables = db.PoolTables.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToList();

        var liveSessions = db.TableSessions.AsNoTracking()
            .Include(s => s.Segments)
            .Include(s => s.Pauses)
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .ToList();

        var userNames = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var byTable = liveSessions.ToDictionary(s => s.CurrentTableId);

        var cards = new List<TableCard>(tables.Count);
        foreach (var table in tables)
        {
            if (byTable.TryGetValue(table.Id, out var session))
            {
                var snapshot = new LiveSessionSnapshot(
                    session.Id,
                    session.SessionNumber,
                    session.CurrentTableId,
                    session.Status == SessionStatus.Paused,
                    session.StartUtc,
                    userNames.GetValueOrDefault(session.OpenedByUserId, string.Empty),
                    session.CustomerLabel,
                    session.Note,
                    session.Policy,
                    session.BillingType,
                    session.FixedAmount,
                    SegmentTimings(session),
                    PauseTimings(session));

                var status = session.Status == SessionStatus.Paused ? DashboardStatus.Paused : DashboardStatus.InUse;
                cards.Add(new TableCard(table.Id, table.Name, table.Type, table.HourlyRate, status, snapshot));
            }
            else
            {
                cards.Add(new TableCard(table.Id, table.Name, table.Type, table.HourlyRate, DashboardStatus.Available, null));
            }
        }

        return cards;
    }

    // ==================== START ====================

    public OperationResult<int> StartSession(StartSessionRequest request)
    {
        using var db = _factory.CreateDbContext();

        var guard = Authorize<int>(db, request.UserId, request.ShiftId, Permission.StartSession);
        if (guard is not null)
        {
            return guard;
        }

        var table = db.PoolTables.FirstOrDefault(t => t.Id == request.TableId);
        if (table is null || !table.IsActive)
        {
            return OperationResult<int>.Failure("That table is not available.");
        }

        if (request.BillingType == BillingType.Fixed)
        {
            if (request.FixedAmount is not { } fixedAmount)
            {
                return OperationResult<int>.Failure("Please enter the fixed charge for this session.");
            }

            if (fixedAmount.IsNegative)
            {
                return OperationResult<int>.Failure("The fixed charge cannot be negative.");
            }
        }

        if (db.TableSessions.Any(s => s.CurrentTableId == request.TableId &&
                                      (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)))
        {
            return OperationResult<int>.Failure("That table is already in use.");
        }

        var now = _clock.UtcNow;
        var settings = db.BillingSettings.AsNoTracking().FirstOrDefault(s => s.Id == 1);
        var policy = settings?.ToPolicy() ?? BillingPolicy.Default;
        var nextNumber = (db.TableSessions.Max(s => (int?)s.SessionNumber) ?? 0) + 1;

        using var tx = db.Database.BeginTransaction();
        try
        {
            var session = new TableSession
            {
                SessionNumber = nextNumber,
                Status = SessionStatus.Active,
                CheckoutStatus = CheckoutStatus.NotCompleted,
                CurrentTableId = table.Id,
                StartUtc = now,
                CustomerLabel = Clean(request.CustomerLabel),
                Note = Clean(request.Note),
                BillingType = request.BillingType,
                FixedAmount = request.BillingType == BillingType.Fixed ? request.FixedAmount : null,
                BillingMethod = policy.Method,
                RoundingIncrementMinutes = policy.RoundingIncrementMinutes,
                MinimumBillableMinutes = policy.MinimumBillableMinutes,
                GracePeriodMinutes = policy.GracePeriodMinutes,
                OpenedByUserId = request.UserId,
                OpenedShiftId = request.ShiftId,
                CreatedUtc = now,
                UpdatedUtc = now,
                Segments =
                {
                    new SessionSegment
                    {
                        SegmentIndex = 0,
                        TableId = table.Id,
                        HourlyRate = table.HourlyRate,
                        StartUtc = now,
                    },
                },
            };
            db.TableSessions.Add(session);
            db.SaveChanges();

            var billingDetail = session.BillingType == BillingType.Fixed
                ? $"fixed charge {session.FixedAmount!.Value.Format()}"
                : $"{table.HourlyRate.Format()}/hr";
            WriteAudit(db, AuditActions.SessionStarted, request.UserId, session.Id,
                $"Session #{session.SessionNumber} started on {table.Name} ({billingDetail}).");
            db.SaveChanges();
            tx.Commit();

            return OperationResult<int>.Success(session.SessionNumber);
        }
        catch (DbUpdateException)
        {
            tx.Rollback();
            // The filtered unique index rejected a concurrent/duplicate start.
            return OperationResult<int>.Failure("That table is already in use.");
        }
    }

    // ==================== PAUSE / RESUME ====================

    public OperationResult PauseSession(int sessionId, int userId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = Authorize(db, userId, shiftId, Permission.PauseResumeSession);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null || !session.IsLive)
        {
            return OperationResult.Failure("That session is not running.");
        }

        if (session.Status == SessionStatus.Paused)
        {
            return OperationResult.Failure("That session is already paused.");
        }

        var now = _clock.UtcNow;
        session.Pauses.Add(new SessionPause
        {
            PausedUtc = now,
            PausedByUserId = userId,
            ShiftId = shiftId,
        });
        session.Status = SessionStatus.Paused;
        session.UpdatedUtc = now;

        WriteAudit(db, AuditActions.SessionPaused, userId, session.Id, $"Session #{session.SessionNumber} paused.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult ResumeSession(int sessionId, int userId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = Authorize(db, userId, shiftId, Permission.PauseResumeSession);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null || !session.IsLive)
        {
            return OperationResult.Failure("That session is not running.");
        }

        if (session.Status != SessionStatus.Paused)
        {
            return OperationResult.Failure("That session is not paused.");
        }

        var now = _clock.UtcNow;
        var openPause = session.Pauses.FirstOrDefault(p => p.ResumedUtc is null);
        if (openPause is not null)
        {
            openPause.ResumedUtc = now;
            openPause.ResumedByUserId = userId;
        }

        session.Status = SessionStatus.Active;
        session.UpdatedUtc = now;

        WriteAudit(db, AuditActions.SessionResumed, userId, session.Id, $"Session #{session.SessionNumber} resumed.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    // ==================== TRANSFER ====================

    public OperationResult TransferSession(int sessionId, int destinationTableId, int userId, int shiftId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure("Please enter a reason for the transfer.");
        }

        using var db = _factory.CreateDbContext();
        var guard = Authorize(db, userId, shiftId, Permission.TransferSession);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null || !session.IsLive)
        {
            return OperationResult.Failure("That session is not running.");
        }

        if (destinationTableId == session.CurrentTableId)
        {
            return OperationResult.Failure("Please choose a different table.");
        }

        var destination = db.PoolTables.FirstOrDefault(t => t.Id == destinationTableId);
        if (destination is null || !destination.IsActive)
        {
            return OperationResult.Failure("The destination table is not available.");
        }

        if (db.TableSessions.Any(s => s.CurrentTableId == destinationTableId &&
                                      (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)))
        {
            return OperationResult.Failure("The destination table is already in use.");
        }

        var now = _clock.UtcNow;
        var sourceTableId = session.CurrentTableId;

        using var tx = db.Database.BeginTransaction();
        try
        {
            var current = session.Segments.OrderByDescending(s => s.SegmentIndex).First();
            current.EndUtc = now;
            current.EndReason = "Transfer";

            session.Segments.Add(new SessionSegment
            {
                SegmentIndex = current.SegmentIndex + 1,
                TableId = destination.Id,
                HourlyRate = destination.HourlyRate,
                StartUtc = now,
            });
            session.CurrentTableId = destination.Id;
            session.UpdatedUtc = now;

            WriteAudit(db, AuditActions.SessionTransferred, userId, session.Id,
                $"Session #{session.SessionNumber} transferred from table {sourceTableId} to {destination.Name} at {destination.HourlyRate.Format()}/hr. Reason: {reason.Trim()}");
            db.SaveChanges();
            tx.Commit();
            return OperationResult.Success();
        }
        catch (DbUpdateException)
        {
            tx.Rollback();
            return OperationResult.Failure("The destination table is already in use.");
        }
    }

    // ==================== FINISH ====================

    public OperationResult<SessionSummary> GetFinishPreview(int sessionId)
    {
        using var db = _factory.CreateDbContext();
        var session = LoadSession(db, sessionId);
        if (session is null || !session.IsLive)
        {
            return OperationResult<SessionSummary>.Failure("That session is not running.");
        }

        var names = TableNames(db);
        return OperationResult<SessionSummary>.Success(Summarize(session, _clock.UtcNow, frozen: false, names));
    }

    public OperationResult<SessionSummary> FinishSession(int sessionId, int userId, int shiftId, string? closingNote)
    {
        using var db = _factory.CreateDbContext();
        var guard = Authorize<SessionSummary>(db, userId, shiftId, Permission.FinishSession);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null || !session.IsLive)
        {
            return OperationResult<SessionSummary>.Failure("That session is not running.");
        }

        var now = _clock.UtcNow;

        using var tx = db.Database.BeginTransaction();
        try
        {
            // Close any open pause and the current segment at the finish time.
            foreach (var pause in session.Pauses.Where(p => p.ResumedUtc is null))
            {
                pause.ResumedUtc = now;
                pause.ResumedByUserId = userId;
            }

            var current = session.Segments.OrderByDescending(s => s.SegmentIndex).First();
            if (current.EndUtc is null)
            {
                current.EndUtc = now;
                current.EndReason = "Finish";
            }

            var calc = _calculator.Calculate(session.Policy, SegmentTimings(session), PauseTimings(session), now);
            var adjustments = SumChargeAdjustments(session);

            session.Status = SessionStatus.Completed;
            session.CheckoutStatus = CheckoutStatus.AwaitingCheckout;
            session.FinishUtc = now;
            session.FinishedByUserId = userId;
            session.FinishedShiftId = shiftId;
            session.ClosingNote = Clean(closingNote);
            session.FinalCharge = BillingResolution.EffectiveCharge(session.BillingType, session.FixedAmount, calc.Charge, adjustments);
            session.FinalBillableSeconds = calc.BillableSeconds;
            session.UpdatedUtc = now;

            WriteAudit(db, AuditActions.SessionFinished, userId, session.Id,
                $"Session #{session.SessionNumber} finished. Charge {session.FinalCharge.Value.Format()} (awaiting checkout).");
            db.SaveChanges();
            tx.Commit();

            var names = TableNames(db);
            return OperationResult<SessionSummary>.Success(Summarize(session, now, frozen: true, names));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Finishing session {SessionId} failed.", sessionId);
            return OperationResult<SessionSummary>.Failure("Could not finish the session. No changes were saved.");
        }
    }

    public SessionSummary? GetSessionSummary(int sessionId)
    {
        using var db = _factory.CreateDbContext();
        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return null;
        }

        var names = TableNames(db);
        var frozen = session.Status is SessionStatus.Completed or SessionStatus.Voided;
        var asOf = session.FinishUtc ?? _clock.UtcNow;
        return Summarize(session, asOf, frozen, names);
    }

    // ==================== HISTORY ====================

    public IReadOnlyList<SessionHistoryItem> GetHistory(SessionHistoryFilter filter)
    {
        using var db = _factory.CreateDbContext();

        var query = db.TableSessions.AsNoTracking()
            .Include(s => s.Segments)
            .Where(s => s.Status == SessionStatus.Completed || s.Status == SessionStatus.Voided);

        if (filter.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }

        if (filter.SessionNumber is { } number)
        {
            query = query.Where(s => s.SessionNumber == number);
        }

        var sessions = query.ToList();

        // Date and table filters applied in memory (SQLite cannot compare/order DateTimeOffset).
        if (filter.FromUtc is { } from)
        {
            sessions = sessions.Where(s => (s.FinishUtc ?? s.StartUtc) >= from).ToList();
        }

        if (filter.ToUtc is { } to)
        {
            sessions = sessions.Where(s => (s.FinishUtc ?? s.StartUtc) <= to).ToList();
        }

        if (filter.TableId is { } tableId)
        {
            sessions = sessions.Where(s => s.Segments.Any(seg => seg.TableId == tableId)).ToList();
        }

        var names = TableNames(db);
        var userNames = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);

        return sessions
            .OrderByDescending(s => s.SessionNumber)
            .Select(s => new SessionHistoryItem(
                s.Id,
                s.SessionNumber,
                s.StartUtc,
                s.FinishUtc,
                string.Join(" → ", s.Segments.OrderBy(seg => seg.SegmentIndex)
                    .Select(seg => names.GetValueOrDefault(seg.TableId, "?")).Distinct()),
                s.FinalBillableSeconds ?? 0,
                s.FinalCharge ?? Money.Zero,
                s.Status,
                s.CheckoutStatus,
                userNames.GetValueOrDefault(s.OpenedByUserId, string.Empty),
                s.FinishedByUserId is { } fid ? userNames.GetValueOrDefault(fid, string.Empty) : null))
            .ToList();
    }

    // ==================== CORRECTIONS ====================

    public SessionCorrectionContext? GetCorrectionContext(int sessionId)
    {
        using var db = _factory.CreateDbContext();
        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return null;
        }

        var names = TableNames(db);
        var frozen = session.Status is SessionStatus.Completed or SessionStatus.Voided;
        var asOf = session.FinishUtc ?? _clock.UtcNow;
        var calc = _calculator.Calculate(session.Policy, SegmentTimings(session), PauseTimings(session), asOf);
        var currentCharge = frozen && session.FinalCharge is { } fc
            ? fc
            : BillingResolution.EffectiveCharge(session.BillingType, session.FixedAmount, calc.Charge, SumChargeAdjustments(session));

        return new SessionCorrectionContext(
            session.Id,
            session.SessionNumber,
            session.Status,
            session.StartUtc,
            session.FinishUtc,
            session.Policy,
            currentCharge,
            session.BillingType,
            session.FixedAmount,
            session.Segments.OrderBy(s => s.SegmentIndex)
                .Select(s => new CorrectionSegmentInfo(s.Id, names.GetValueOrDefault(s.TableId, "?"), s.HourlyRate, s.StartUtc, s.EndUtc))
                .ToList(),
            session.Pauses.OrderBy(p => p.PausedUtc)
                .Select(p => new CorrectionPauseInfo(p.Id, p.PausedUtc, p.ResumedUtc))
                .ToList());
    }

    public OperationResult CorrectStartTime(int sessionId, DateTimeOffset newStartUtc, string reason, int actorUserId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return OperationResult.Failure("That session was not found.");
        }

        var now = _clock.UtcNow;
        var old = session.StartUtc;
        session.StartUtc = newStartUtc;
        var first = session.Segments.OrderBy(s => s.SegmentIndex).First();
        first.StartUtc = newStartUtc;

        var timeline = ValidateTimeline(session, now);
        if (timeline.Count > 0)
        {
            return OperationResult.Failure(timeline);
        }

        session.UpdatedUtc = now;
        RecordAdjustment(db, session, SessionAdjustmentType.StartTimeCorrection, reason, actor,
            old.ToString("O"), newStartUtc.ToString("O"), null, now, shiftId);
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult CorrectPauseStart(int pauseId, DateTimeOffset newPausedUtc, string reason, int actorUserId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var pauseRow = db.SessionPauses.FirstOrDefault(p => p.Id == pauseId);
        if (pauseRow is null)
        {
            return OperationResult.Failure("That pause was not found.");
        }

        var session = LoadSession(db, pauseRow.SessionId)!;
        var pause = session.Pauses.First(p => p.Id == pauseId);
        var now = _clock.UtcNow;
        var old = pause.PausedUtc;
        pause.PausedUtc = newPausedUtc;

        var timeline = ValidateTimeline(session, now);
        if (timeline.Count > 0)
        {
            return OperationResult.Failure(timeline);
        }

        session.UpdatedUtc = now;
        RecordAdjustment(db, session, SessionAdjustmentType.PauseResumeCorrection, reason, actor,
            $"Pause start {old:O}", $"Pause start {newPausedUtc:O}", null, now, shiftId);
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult CorrectPauseEnd(int pauseId, DateTimeOffset newResumedUtc, string reason, int actorUserId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var pauseRow = db.SessionPauses.FirstOrDefault(p => p.Id == pauseId);
        if (pauseRow is null)
        {
            return OperationResult.Failure("That pause was not found.");
        }

        var session = LoadSession(db, pauseRow.SessionId)!;
        var pause = session.Pauses.First(p => p.Id == pauseId);
        if (pause.ResumedUtc is null)
        {
            return OperationResult.Failure("This pause is still open. Resume it before correcting its end time.");
        }

        var now = _clock.UtcNow;
        var old = pause.ResumedUtc.Value;
        pause.ResumedUtc = newResumedUtc;

        var timeline = ValidateTimeline(session, now);
        if (timeline.Count > 0)
        {
            return OperationResult.Failure(timeline);
        }

        session.UpdatedUtc = now;
        RecordAdjustment(db, session, SessionAdjustmentType.PauseResumeCorrection, reason, actor,
            $"Pause end {old:O}", $"Pause end {newResumedUtc:O}", null, now, shiftId);
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult CorrectSegmentRate(int segmentId, Money newRate, string reason, int actorUserId, int shiftId)
    {
        if (newRate.IsNegative)
        {
            return OperationResult.Failure("The rate cannot be negative.");
        }

        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var segment = db.SessionSegments.FirstOrDefault(s => s.Id == segmentId);
        if (segment is null)
        {
            return OperationResult.Failure("That segment was not found.");
        }

        var session = LoadSession(db, segment.SessionId)!;
        var now = _clock.UtcNow;
        var old = segment.HourlyRate;
        // Update the tracked segment inside the loaded session graph.
        var tracked = session.Segments.First(s => s.Id == segmentId);
        tracked.HourlyRate = newRate;
        session.UpdatedUtc = now;

        RecordAdjustment(db, session, SessionAdjustmentType.RateCorrection, reason, actor,
            old.Format(), newRate.Format(), null, now, shiftId);
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult CorrectFixedAmount(int sessionId, Money newFixedAmount, string reason, int actorUserId, int shiftId)
    {
        if (newFixedAmount.IsNegative)
        {
            return OperationResult.Failure("The fixed charge cannot be negative.");
        }

        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return OperationResult.Failure("That session was not found.");
        }

        if (session.BillingType != BillingType.Fixed)
        {
            return OperationResult.Failure("This session is billed hourly. Switch it to a fixed charge first.");
        }

        var now = _clock.UtcNow;
        var old = session.FixedAmount ?? Money.Zero;
        session.FixedAmount = newFixedAmount;
        session.UpdatedUtc = now;

        RecordAdjustment(db, session, SessionAdjustmentType.FixedAmountCorrection, reason, actor,
            old.Format(), newFixedAmount.Format(), null, now, shiftId);
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult CorrectBillingType(int sessionId, BillingType newType, Money? newFixedAmount, string reason, int actorUserId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return OperationResult.Failure("That session was not found.");
        }

        if (session.BillingType == newType)
        {
            return OperationResult.Failure($"This session is already billed {DescribeBilling(newType)}.");
        }

        if (newType == BillingType.Fixed)
        {
            if (newFixedAmount is not { } amount)
            {
                return OperationResult.Failure("Please enter the fixed charge to switch to.");
            }

            if (amount.IsNegative)
            {
                return OperationResult.Failure("The fixed charge cannot be negative.");
            }
        }

        var now = _clock.UtcNow;
        var oldDescription = DescribeBillingValue(session);

        session.BillingType = newType;
        session.FixedAmount = newType == BillingType.Fixed ? newFixedAmount : null;
        session.UpdatedUtc = now;

        var newDescription = DescribeBillingValue(session);
        RecordAdjustment(db, session, SessionAdjustmentType.BillingTypeCorrection, reason, actor,
            oldDescription, newDescription, null, now, shiftId);
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    private static string DescribeBilling(BillingType type) => type == BillingType.Fixed ? "as a fixed charge" : "hourly";

    private static string DescribeBillingValue(TableSession session) => session.BillingType == BillingType.Fixed
        ? $"Fixed {(session.FixedAmount ?? Money.Zero).Format()}"
        : "Hourly";

    public OperationResult AddChargeAdjustment(int sessionId, Money amount, string reason, int actorUserId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return OperationResult.Failure("That session was not found.");
        }

        var now = _clock.UtcNow;
        RecordAdjustment(db, session, SessionAdjustmentType.ChargeAdjustment, reason, actor,
            null, amount.Format(), amount, now, shiftId);
        session.UpdatedUtc = now;
        RecomputeIfFrozen(session);
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult VoidSession(int sessionId, string reason, int actorUserId, int shiftId)
    {
        using var db = _factory.CreateDbContext();
        var guard = AuthorizeCorrection(db, actorUserId, shiftId, reason, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var session = LoadSession(db, sessionId);
        if (session is null)
        {
            return OperationResult.Failure("That session was not found.");
        }

        if (session.Status == SessionStatus.Voided)
        {
            return OperationResult.Failure("That session is already voided.");
        }

        var now = _clock.UtcNow;
        foreach (var pause in session.Pauses.Where(p => p.ResumedUtc is null))
        {
            pause.ResumedUtc = now;
        }

        var current = session.Segments.OrderByDescending(s => s.SegmentIndex).First();
        current.EndUtc ??= now;

        session.Status = SessionStatus.Voided;
        session.CheckoutStatus = CheckoutStatus.NotCompleted;
        session.VoidReason = reason.Trim();
        session.VoidedByUserId = actorUserId;
        session.VoidedUtc = now;
        session.FinalCharge = Money.Zero;
        session.FinalBillableSeconds = 0;
        session.UpdatedUtc = now;

        RecordAdjustment(db, session, SessionAdjustmentType.Void, reason, actor, null, null, null, now, shiftId);
        db.SaveChanges();
        return OperationResult.Success();
    }

    // ==================== HELPERS ====================

    private TableSession? LoadSession(SnookerPointDbContext db, int sessionId) =>
        db.TableSessions
            .Include(s => s.Segments)
            .Include(s => s.Pauses)
            .Include(s => s.Adjustments)
            .FirstOrDefault(s => s.Id == sessionId);

    private static List<SegmentTiming> SegmentTimings(TableSession session) =>
        session.Segments.OrderBy(s => s.SegmentIndex)
            .Select(s => new SegmentTiming(s.HourlyRate, s.StartUtc, s.EndUtc))
            .ToList();

    private static List<PauseInterval> PauseTimings(TableSession session) =>
        session.Pauses.OrderBy(p => p.PausedUtc)
            .Select(p => new PauseInterval(p.PausedUtc, p.ResumedUtc))
            .ToList();

    private static IReadOnlyList<string> ValidateTimeline(TableSession session, DateTimeOffset now) =>
        SessionTimelineValidator.Validate(
            session.StartUtc,
            session.FinishUtc,
            session.Segments.OrderBy(s => s.SegmentIndex)
                .Select(s => new SessionTimelineValidator.Interval(s.StartUtc, s.EndUtc)).ToList(),
            session.Pauses.OrderBy(p => p.PausedUtc)
                .Select(p => new SessionTimelineValidator.Interval(p.PausedUtc, p.ResumedUtc)).ToList(),
            now);

    private static Money SumChargeAdjustments(TableSession session) =>
        session.Adjustments
            .Where(a => a.Type == SessionAdjustmentType.ChargeAdjustment && a.Amount is not null)
            .Aggregate(Money.Zero, (acc, a) => acc + a.Amount!.Value);

    private static Dictionary<int, string> TableNames(SnookerPointDbContext db) =>
        db.PoolTables.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);

    private SessionSummary Summarize(TableSession session, DateTimeOffset asOf, bool frozen, Dictionary<int, string> tableNames)
    {
        var segments = SegmentTimings(session);
        var pauses = PauseTimings(session);
        var calc = _calculator.Calculate(session.Policy, segments, pauses, asOf);

        var lines = session.Segments.OrderBy(s => s.SegmentIndex)
            .Select(s => new RateSegmentLine(
                tableNames.GetValueOrDefault(s.TableId, "?"),
                s.HourlyRate,
                ActiveSecondsForSegment(s.StartUtc, s.EndUtc ?? asOf, pauses, asOf)))
            .ToList();

        var liveCharge = BillingResolution.EffectiveCharge(
            session.BillingType, session.FixedAmount, calc.Charge, SumChargeAdjustments(session));
        var charge = frozen && session.FinalCharge is { } fc ? fc : liveCharge;
        var billable = frozen && session.FinalBillableSeconds is { } fb ? fb : calc.BillableSeconds;

        return new SessionSummary(
            session.Id,
            session.SessionNumber,
            session.StartUtc,
            session.FinishUtc,
            calc.ElapsedSeconds,
            calc.PausedSeconds,
            billable,
            charge,
            lines,
            session.CustomerLabel,
            session.Note,
            frozen,
            session.BillingType,
            session.FixedAmount);
    }

    private static long ActiveSecondsForSegment(DateTimeOffset start, DateTimeOffset end, List<PauseInterval> pauses, DateTimeOffset asOf)
    {
        var wall = WholeSeconds(start, end);
        long paused = 0;
        foreach (var p in pauses)
        {
            paused += OverlapSeconds(p.Start, p.End ?? asOf, start, end);
        }

        return Math.Max(0, wall - paused);
    }

    private static long WholeSeconds(DateTimeOffset start, DateTimeOffset end)
    {
        var ticks = (end - start).Ticks;
        return ticks <= 0 ? 0 : ticks / TimeSpan.TicksPerSecond;
    }

    private static long OverlapSeconds(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return WholeSeconds(start, end);
    }

    private void RecomputeIfFrozen(TableSession session)
    {
        if (session.Status is not (SessionStatus.Completed or SessionStatus.Voided))
        {
            return;
        }

        if (session.Status == SessionStatus.Voided)
        {
            session.FinalCharge = Money.Zero;
            session.FinalBillableSeconds = 0;
            return;
        }

        var asOf = session.FinishUtc ?? _clock.UtcNow;
        var calc = _calculator.Calculate(session.Policy, SegmentTimings(session), PauseTimings(session), asOf);
        session.FinalCharge = BillingResolution.EffectiveCharge(
            session.BillingType, session.FixedAmount, calc.Charge, SumChargeAdjustments(session));
        session.FinalBillableSeconds = calc.BillableSeconds;
    }

    // --- Authorization guards ---

    private OperationResult? Authorize(SnookerPointDbContext db, int userId, int shiftId, Permission permission)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || !_permissions.HasPermission(user, permission))
        {
            return OperationResult.Failure("You do not have permission to do that.");
        }

        if (!IsOpenShift(db, shiftId))
        {
            return OperationResult.Failure("You need an open shift to do that.");
        }

        return null;
    }

    private OperationResult<T>? Authorize<T>(SnookerPointDbContext db, int userId, int shiftId, Permission permission)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || !_permissions.HasPermission(user, permission))
        {
            return OperationResult<T>.Failure("You do not have permission to do that.");
        }

        if (!IsOpenShift(db, shiftId))
        {
            return OperationResult<T>.Failure("You need an open shift to do that.");
        }

        return null;
    }

    private OperationResult? AuthorizeCorrection(SnookerPointDbContext db, int actorUserId, int shiftId, string reason, out User actor)
    {
        actor = null!;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure("A reason is required for a correction.");
        }

        var user = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (user is null || !_permissions.HasPermission(user, Permission.CorrectSession))
        {
            return OperationResult.Failure("You do not have permission to correct sessions.");
        }

        if (!IsOpenShift(db, shiftId))
        {
            return OperationResult.Failure("You need an open shift to correct a session.");
        }

        actor = user;
        return null;
    }

    private static bool IsOpenShift(SnookerPointDbContext db, int shiftId) =>
        db.Shifts.Any(s => s.Id == shiftId && s.Status == ShiftStatus.Open);

    private void RecordAdjustment(
        SnookerPointDbContext db, TableSession session, SessionAdjustmentType type, string reason,
        User actor, string? oldValue, string? newValue, Money? amount, DateTimeOffset now, int shiftId)
    {
        session.Adjustments.Add(new SessionAdjustment
        {
            Type = type,
            Reason = reason.Trim(),
            OldValue = oldValue,
            NewValue = newValue,
            Amount = amount,
            ApprovedByUserId = actor.Id,
            ShiftId = shiftId,
            Utc = now,
        });

        WriteAudit(db, type == SessionAdjustmentType.Void ? AuditActions.SessionVoided : AuditActions.SessionCorrected,
            actor.Id, session.Id, $"Session #{session.SessionNumber} {type}. Reason: {reason.Trim()}");
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int sessionId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(TableSession),
            EntityId = sessionId.ToString(),
            Details = details,
        });
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
