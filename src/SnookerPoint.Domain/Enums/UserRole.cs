namespace SnookerPoint.Domain.Enums;

/// <summary>
/// The staff roles available in the first release. A user has exactly one primary
/// role; capabilities are resolved through permissions (see
/// <see cref="Security.RolePermissions"/>) rather than by checking role names.
/// </summary>
public enum UserRole
{
    Owner = 0,
    Administrator = 1,
    Manager = 2,
    Cashier = 3,
    FloorStaff = 4,
}
