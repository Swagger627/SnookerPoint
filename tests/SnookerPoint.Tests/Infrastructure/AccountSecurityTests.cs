using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

/// <summary>
/// Section D/E/F account-security tests: a user changes their own password/PIN after
/// re-entering the current password; owners reset staff credentials and issue temporary
/// passwords; only an Owner may act on another Owner; and Owner recovery codes are stored
/// only as hashes, rate-limited, single-use and audited without leaking any secret.
/// </summary>
public class AccountSecurityTests
{
    private const string OwnerPassword = "secret123";

    private static int CreateStaff(Phase1Environment env, int ownerId, string username, UserRole role, string password, string? pin = null) =>
        env.StaffManagement.CreateStaff(new CreateStaffRequest($"User {username}", username, role, password, pin), ownerId).Value;

    // ---------- Self-service password / PIN ----------

    [Fact]
    public void ChangePassword_WithCorrectCurrent_Succeeds()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.AccountSecurity.ChangePassword(ownerId, OwnerPassword, "newpass1");

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(env.Auth.LoginWithPassword("owner", OwnerPassword).Succeeded);
        Assert.True(env.Auth.LoginWithPassword("owner", "newpass1").Succeeded);
    }

    [Fact]
    public void ChangePassword_WithWrongCurrent_IsRejected_AndLeavesPasswordIntact()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.AccountSecurity.ChangePassword(ownerId, "not-my-password", "newpass1");

        Assert.True(result.Failed);
        Assert.True(env.Auth.LoginWithPassword("owner", OwnerPassword).Succeeded); // unchanged
    }

    [Fact]
    public void ChangePin_ThenLoginWithPin_Works()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        Assert.True(env.AccountSecurity.ChangePin(ownerId, OwnerPassword, "4321").Succeeded);

        Assert.True(env.Auth.LoginWithPin("owner", "4321").Succeeded);
    }

    [Fact]
    public void ChangePin_WithWrongCurrentPassword_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.AccountSecurity.ChangePin(ownerId, "wrong", "4321");
        Assert.True(result.Failed);
    }

    [Fact]
    public void RemovePin_DisablesPinLogin()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        Assert.True(env.AccountSecurity.ChangePin(ownerId, OwnerPassword, "4321").Succeeded);

        Assert.True(env.AccountSecurity.RemovePin(ownerId, OwnerPassword).Succeeded);

        var login = env.Auth.LoginWithPin("owner", "4321");
        Assert.False(login.Succeeded);
    }

    // ---------- Owner-managed staff resets ----------

    [Fact]
    public void Owner_ResetsStaffPassword()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var staffId = CreateStaff(env, ownerId, "rita", UserRole.Cashier, "oldpass1");

        Assert.True(env.StaffManagement.SetPassword(staffId, "resetme1", ownerId).Succeeded);

        Assert.False(env.Auth.LoginWithPassword("rita", "oldpass1").Succeeded);
        Assert.True(env.Auth.LoginWithPassword("rita", "resetme1").Succeeded);
    }

    [Fact]
    public void Owner_ResetsStaffPin()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var staffId = CreateStaff(env, ownerId, "rita", UserRole.Cashier, "oldpass1");

        Assert.True(env.StaffManagement.SetPin(staffId, "9876", ownerId).Succeeded);
        Assert.True(env.Auth.LoginWithPin("rita", "9876").Succeeded);
    }

    [Fact]
    public void TemporaryPassword_RequiresChangeAtNextLogin()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var staffId = CreateStaff(env, ownerId, "temp", UserRole.Cashier, "oldpass1");

        var temp = env.StaffManagement.GenerateTemporaryPassword(staffId, ownerId);
        Assert.True(temp.Succeeded, temp.ErrorMessage);

        var login = env.Auth.LoginWithPassword("temp", temp.Value!);
        Assert.True(login.Succeeded, "temporary password should log in");
        Assert.True(login.User!.MustChangePassword);

        // Changing it clears the flag; a subsequent login is normal.
        Assert.True(env.AccountSecurity.ChangePassword(staffId, temp.Value!, "chosen01").Succeeded);
        var after = env.Auth.LoginWithPassword("temp", "chosen01");
        Assert.True(after.Succeeded);
        Assert.False(after.User!.MustChangePassword);
    }

    [Fact]
    public void Administrator_CannotResetAnOwner()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var adminId = CreateStaff(env, ownerId, "ada", UserRole.Administrator, "adminpass1");
        var otherOwnerId = CreateStaff(env, ownerId, "boss2", UserRole.Owner, "bosspass1");

        var result = env.StaffManagement.SetPassword(otherOwnerId, "hijack01", adminId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Owner_CanResetAnotherOwner()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var otherOwnerId = CreateStaff(env, ownerId, "boss2", UserRole.Owner, "bosspass1");

        var result = env.StaffManagement.SetPassword(otherOwnerId, "reset123", ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(env.Auth.LoginWithPassword("boss2", "reset123").Succeeded);
    }

    // ---------- Owner recovery code ----------

    [Fact]
    public void RecoveryCode_IsStoredOnlyAsAHash()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var gen = env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword);
        Assert.True(gen.Succeeded, gen.ErrorMessage);
        var code = gen.Value!;

        using var db = env.NewContext();
        var user = db.Users.Single(u => u.Id == ownerId);
        Assert.NotNull(user.RecoveryCodeHash);
        Assert.DoesNotContain(code, user.RecoveryCodeHash!);          // plaintext never stored
        Assert.DoesNotContain(code.Replace("-", ""), user.RecoveryCodeHash!);
    }

    [Fact]
    public void CorrectRecoveryCode_RecoversTheAccount()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var code = env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword).Value!;

        var result = env.OwnerRecovery.Recover("owner", code, "recovered1", null);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(env.Auth.LoginWithPassword("owner", "recovered1").Succeeded);
    }

    [Fact]
    public void WrongRecoveryCode_Fails()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword);

        var result = env.OwnerRecovery.Recover("owner", "0000-0000-0000-0000-0000", "recovered1", null);

        Assert.True(result.Failed);
        Assert.True(env.Auth.LoginWithPassword("owner", OwnerPassword).Succeeded); // password unchanged
    }

    [Fact]
    public void RecoveryAttempts_AreRateLimited()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var code = env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword).Value!;

        // Exhaust the allowed failed attempts with valid new credentials so the code check is reached.
        for (var i = 0; i < 5; i++)
        {
            env.OwnerRecovery.Recover("owner", "0000-0000-0000-0000-0000", "recovered1", null);
        }

        // Now even the correct code is refused because the account is locked out.
        var result = env.OwnerRecovery.Recover("owner", code, "recovered1", null);
        Assert.True(result.Failed);
        Assert.Contains("Too many", result.ErrorMessage);
    }

    [Fact]
    public void SuccessfulRecovery_InvalidatesTheUsedCode()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var code = env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword).Value!;

        var first = env.OwnerRecovery.Recover("owner", code, "recovered1", null);
        Assert.True(first.Succeeded, first.ErrorMessage);

        // The same code cannot be used a second time (a fresh code was issued).
        var replay = env.OwnerRecovery.Recover("owner", code, "recovered2", null);
        Assert.True(replay.Failed);

        // ...but the replacement code returned by the first recovery works.
        var withReplacement = env.OwnerRecovery.Recover("owner", first.Value!.NewRecoveryCode, "recovered3", null);
        Assert.True(withReplacement.Succeeded, withReplacement.ErrorMessage);
    }

    [Fact]
    public void RegeneratingCode_InvalidatesThePreviousOne()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var first = env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword).Value!;
        var second = env.OwnerRecovery.RegenerateCode(ownerId, OwnerPassword).Value!;

        Assert.NotEqual(first, second);
        Assert.True(env.OwnerRecovery.Recover("owner", first, "recovered1", null).Failed);   // old code dead
        Assert.True(env.OwnerRecovery.Recover("owner", second, "recovered1", null).Succeeded); // new code lives
    }

    [Fact]
    public void RegenerateCode_RequiresCorrectPassword()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.OwnerRecovery.RegenerateCode(ownerId, "wrong-password");
        Assert.True(result.Failed);
    }

    // ---------- Audit hygiene ----------

    [Fact]
    public void AuditEvents_ContainNoSecretValues()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var staffId = CreateStaff(env, ownerId, "aud", UserRole.Cashier, "oldpass1");

        Assert.True(env.AccountSecurity.ChangePassword(ownerId, OwnerPassword, "topsecret9").Succeeded);
        Assert.True(env.AccountSecurity.ChangePin(ownerId, "topsecret9", "1379").Succeeded);
        var temp = env.StaffManagement.GenerateTemporaryPassword(staffId, ownerId).Value!;
        var code = env.OwnerRecovery.RegenerateCode(ownerId, "topsecret9").Value!;
        env.OwnerRecovery.Recover("owner", code, "afterrec1", "2468");

        using var db = env.NewContext();
        var secrets = new[] { "topsecret9", "1379", temp, code, code.Replace("-", ""), "afterrec1", "2468" };
        foreach (var audit in db.AuditEvents.ToList())
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, audit.Details ?? string.Empty);
            }
        }
    }
}
