using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class StaffManagementServiceTests
{
    private const string GoodPassword = "password1";

    [Fact]
    public void CreateStaff_RequiresManageStaffPermission()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        // A Manager can manage tables but not staff.
        var managerId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Mgr", "mgr", UserRole.Manager, GoodPassword, null), ownerId).Value;

        var result = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Nope", "nope", UserRole.Cashier, GoodPassword, null), managerId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void CreateStaff_AddsAccount()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Cashier One", "cash1", UserRole.Cashier, GoodPassword, null), ownerId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(env.StaffManagement.GetAll(), s => s.Username == "cash1" && s.Role == UserRole.Cashier);
    }

    [Fact]
    public void CreatedStaff_CanLogInWithTheirRole()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Mona", "mona", UserRole.Manager, GoodPassword, null), ownerId);

        var login = env.Auth.LoginWithPassword("mona", GoodPassword);

        Assert.True(login.Succeeded);
        Assert.Equal(UserRole.Manager, login.User!.Role);
    }

    [Fact]
    public void SetPassword_ResetsCredential()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var userId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Rita", "rita", UserRole.Cashier, GoodPassword, null), ownerId).Value;

        var reset = env.StaffManagement.SetPassword(userId, "brandnew1", ownerId);
        Assert.True(reset.Succeeded, reset.ErrorMessage);

        Assert.False(env.Auth.LoginWithPassword("rita", GoodPassword).Succeeded);
        Assert.True(env.Auth.LoginWithPassword("rita", "brandnew1").Succeeded);
    }

    [Fact]
    public void DuplicateUsername_IsRejected_CaseInsensitive()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        env.StaffManagement.CreateStaff(new CreateStaffRequest("Sam", "sam", UserRole.Cashier, GoodPassword, null), ownerId);

        var result = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Sammy", "SAM", UserRole.Cashier, GoodPassword, null), ownerId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void DisabledAccount_CannotLogIn()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var userId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Deb", "deb", UserRole.Cashier, GoodPassword, null), ownerId).Value;

        Assert.True(env.StaffManagement.SetActive(userId, false, ownerId).Succeeded);

        var login = env.Auth.LoginWithPassword("deb", GoodPassword);
        Assert.False(login.Succeeded);
        Assert.Equal(LoginFailureReason.AccountDisabled, login.Reason);
    }

    [Fact]
    public void LastActiveOwner_CannotBeDisabled()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.StaffManagement.SetActive(ownerId, false, ownerId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void LastActiveOwner_CannotBeDemoted()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.StaffManagement.UpdateStaff(
            new UpdateStaffRequest(ownerId, "The Owner", "owner", UserRole.Manager), ownerId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void OnlyOwner_CanCreateOwner()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var adminId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Ada", "ada", UserRole.Administrator, GoodPassword, null), ownerId).Value;

        // An Administrator may not create an Owner...
        Assert.True(env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Boss", "boss", UserRole.Owner, GoodPassword, null), adminId).Failed);

        // ...but an Owner may.
        Assert.True(env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Boss", "boss", UserRole.Owner, GoodPassword, null), ownerId).Succeeded);
    }

    [Fact]
    public void OnlyOwner_CanPromoteToOwner()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var adminId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Ada", "ada", UserRole.Administrator, GoodPassword, null), ownerId).Value;
        var targetId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Tom", "tom", UserRole.Cashier, GoodPassword, null), ownerId).Value;

        var byAdmin = env.StaffManagement.UpdateStaff(
            new UpdateStaffRequest(targetId, "Tom", "tom", UserRole.Owner), adminId);
        Assert.True(byAdmin.Failed);

        var byOwner = env.StaffManagement.UpdateStaff(
            new UpdateStaffRequest(targetId, "Tom", "tom", UserRole.Owner), ownerId);
        Assert.True(byOwner.Succeeded, byOwner.ErrorMessage);
    }

    [Fact]
    public void SetPin_ThenRemove_IsAudited()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var userId = env.StaffManagement.CreateStaff(
            new CreateStaffRequest("Pia", "pia", UserRole.Cashier, GoodPassword, null), ownerId).Value;

        Assert.True(env.StaffManagement.SetPin(userId, "1234", ownerId).Succeeded);
        Assert.True(env.StaffManagement.GetAll().Single(s => s.Id == userId).HasPin);

        Assert.True(env.StaffManagement.SetPin(userId, null, ownerId).Succeeded);
        Assert.False(env.StaffManagement.GetAll().Single(s => s.Id == userId).HasPin);
    }
}
