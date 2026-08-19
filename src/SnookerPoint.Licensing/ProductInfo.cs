namespace SnookerPoint.Licensing;

/// <summary>
/// Product-wide licensing constants. These are safe to embed in the customer application —
/// they contain no secrets and no private key material.
/// </summary>
public static class ProductInfo
{
    /// <summary>Stable product identifier a licence must match.</summary>
    public const string ProductId = "SNOOKERPOINT";

    /// <summary>The licence payload format this build writes.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Payload format versions this build can verify.</summary>
    public static readonly IReadOnlyCollection<int> SupportedFormatVersions = new[] { 1 };

    /// <summary>Signature algorithm identifier stored in the payload.</summary>
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256";

    /// <summary>Exact trial length: 72 hours.</summary>
    public static readonly TimeSpan TrialDuration = TimeSpan.FromHours(72);

    /// <summary>How close to expiry the "expiring soon" warning begins.</summary>
    public static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Tolerance for ordinary backwards clock adjustments before a rollback is flagged. Small,
    /// so genuine tampering is caught, but forgiving of routine time-sync corrections.
    /// </summary>
    public static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(10);

    /// <summary>Current version of the protected local state schema.</summary>
    public const int StateVersion = 1;

    /// <summary>Current version of the machine-fingerprint scheme.</summary>
    public const int FingerprintVersion = 1;
}
