using SnookerPoint.App.Services;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>A read-only staff row for the Staff Management list. Carries no secrets.</summary>
public sealed class StaffRowViewModel
{
    private readonly StaffListItem _item;

    public StaffRowViewModel(StaffListItem item)
    {
        _item = item;
    }

    public int Id => _item.Id;
    public string DisplayName => _item.DisplayName;
    public string Username => _item.Username;
    public UserRole Role => _item.Role;
    public bool IsActive => _item.IsActive;
    public bool IsLastActiveOwner => _item.IsLastActiveOwner;

    public string RoleText => Role switch
    {
        UserRole.Owner => "Owner",
        UserRole.Administrator => "Administrator",
        UserRole.Manager => "Manager",
        UserRole.Cashier => "Cashier",
        UserRole.FloorStaff => "Floor Staff",
        _ => Role.ToString(),
    };

    public string StatusText
    {
        get
        {
            if (!IsActive)
            {
                return "Disabled";
            }

            if (_item.IsLockedOut)
            {
                return _item.LockedOutUntilUtc is { } until
                    ? $"Locked until {DisplayFormat.LocalTime(until)}"
                    : "Locked out";
            }

            return "Active";
        }
    }

    public string PinText => _item.HasPin ? "PIN set" : "No PIN";

    public bool IsLockedOut => _item.IsLockedOut;

    /// <summary>Label for the enable/disable button.</summary>
    public string ActiveToggleText => IsActive ? "Disable" : "Enable";

    /// <summary>The last active Owner can never be disabled.</summary>
    public bool CanToggleActive => !(IsActive && IsLastActiveOwner);
}
