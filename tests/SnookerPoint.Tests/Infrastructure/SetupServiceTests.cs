using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class SetupServiceTests
{
    [Fact]
    public void CompleteSetup_Succeeds_AndMarksSetupComplete()
    {
        using var env = new Phase1Environment();

        Assert.False(env.Setup.IsSetupComplete());

        var result = env.Setup.CompleteSetup(SetupRequests.Valid());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(env.Setup.IsSetupComplete());
    }

    [Fact]
    public void CompleteSetup_CreatesFiveDefaultTables()
    {
        using var env = new Phase1Environment();

        env.Setup.CompleteSetup(SetupRequests.Valid());

        using var db = env.NewContext();
        Assert.Equal(5, db.PoolTables.Count());
    }

    [Fact]
    public void CompleteSetup_CanCreateMoreThanFiveTables()
    {
        using var env = new Phase1Environment();

        var request = SetupRequests.Valid(tables: SetupRequests.DefaultTables(count: 8));
        var result = env.Setup.CompleteSetup(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        using var db = env.NewContext();
        Assert.Equal(8, db.PoolTables.Count());
    }

    [Fact]
    public void CompleteSetup_CreatesOwnerUserWithHashedSecrets()
    {
        using var env = new Phase1Environment();

        env.Setup.CompleteSetup(SetupRequests.Valid(username: "boss", password: "secret123", pin: "1234"));

        using var db = env.NewContext();
        var owner = db.Users.Single();
        Assert.Equal("boss", owner.Username);
        Assert.Equal(UserRole.Owner, owner.Role);
        Assert.NotEqual("secret123", owner.PasswordHash);
        Assert.DoesNotContain("secret123", owner.PasswordHash);
        Assert.NotNull(owner.PinHash);
        Assert.DoesNotContain("1234", owner.PinHash!);
    }

    [Fact]
    public void CompleteSetup_WritesAuditEvent()
    {
        using var env = new Phase1Environment();

        env.Setup.CompleteSetup(SetupRequests.Valid());

        using var db = env.NewContext();
        Assert.Contains(db.AuditEvents, e => e.Action == "SetupCompleted");
    }

    [Fact]
    public void CompleteSetup_RejectsDuplicateActiveTableNames()
    {
        using var env = new Phase1Environment();

        var tables = new List<SnookerPoint.Application.Setup.SetupTableInput>
        {
            new("Table 1", TableType.Snooker, Money.FromRupees(500L), true),
            new("table 1", TableType.Pool, Money.FromRupees(500L), true),
        };

        var result = env.Setup.CompleteSetup(SetupRequests.Valid(tables: tables));

        Assert.True(result.Failed);
        Assert.False(env.Setup.IsSetupComplete());
    }

    [Fact]
    public void CompleteSetup_RejectsNegativeTableRate()
    {
        using var env = new Phase1Environment();

        var tables = new List<SnookerPoint.Application.Setup.SetupTableInput>
        {
            new("Table 1", TableType.Snooker, Money.FromPaisa(-100), true),
        };

        var result = env.Setup.CompleteSetup(SetupRequests.Valid(tables: tables));

        Assert.True(result.Failed);
        Assert.False(env.Setup.IsSetupComplete());
    }

    [Fact]
    public void CompleteSetup_RejectsShortPassword()
    {
        using var env = new Phase1Environment();

        var result = env.Setup.CompleteSetup(SetupRequests.Valid(password: "123"));

        Assert.True(result.Failed);
        Assert.False(env.Setup.IsSetupComplete());
    }

    [Fact]
    public void CompleteSetup_SecondTime_IsRejected_AndDoesNotReRun()
    {
        using var env = new Phase1Environment();

        Assert.True(env.Setup.CompleteSetup(SetupRequests.Valid(username: "owner")).Succeeded);

        var second = env.Setup.CompleteSetup(SetupRequests.Valid(username: "someoneelse"));

        Assert.True(second.Failed);
        using var db = env.NewContext();
        Assert.Single(db.Users); // only the original owner
    }

    [Fact]
    public void FailedSetup_RollsBack_LeavingNoPartialData()
    {
        using var env = new Phase1Environment();

        // Invalid owner password forces a validation failure before any write.
        var result = env.Setup.CompleteSetup(SetupRequests.Valid(password: "x"));

        Assert.True(result.Failed);
        using var db = env.NewContext();
        Assert.Empty(db.ClubSettings);
        Assert.Empty(db.PoolTables);
        Assert.Empty(db.Users);
        Assert.False(env.Setup.IsSetupComplete());
    }
}
