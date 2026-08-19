using SnookerPoint.App.Navigation;
using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Default central licensing gate. Delegates to <see cref="ILicensingService"/> and, when
/// operations are blocked, routes to the Activation screen. This is the one place operational
/// entry points consult, so the runtime check is consistent everywhere.
/// </summary>
public sealed class LicenseGate : ILicenseGate
{
    private readonly ILicensingService _licensing;
    private readonly INavigationService _navigation;
    private LicenseEvaluation? _last;

    public LicenseGate(ILicensingService licensing, INavigationService navigation)
    {
        _licensing = licensing;
        _navigation = navigation;
    }

    public LicenseEvaluation Evaluate()
    {
        _last = _licensing.Evaluate();
        return _last;
    }

    public bool OperationsAllowed => (_last ?? Evaluate()).OperationsAllowed;

    public bool EnsureCanOperate()
    {
        var evaluation = Evaluate();
        if (evaluation.OperationsAllowed)
        {
            return true;
        }

        // Blocked: the licensing service already audits the expiry/state event on Evaluate; route
        // the user to Activation so they can reactivate (existing drafts/sessions are persisted).
        _navigation.ShowActivation();
        return false;
    }
}
