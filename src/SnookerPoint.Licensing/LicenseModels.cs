namespace SnookerPoint.Licensing;

/// <summary>The kind of licence. Only lifetime, one-machine licences are issued in this phase.</summary>
public enum LicenseType
{
    Lifetime = 1,
}

/// <summary>
/// The overall licensing state shown to the app. Derived only after signature verification —
/// no field is trusted before the signature checks out.
/// </summary>
public enum LicenseStatus
{
    NotStarted,
    Active,
    ExpiringSoon,
    Expired,
    Licensed,
    InvalidLicense,
    MachineMismatch,
    LicenseStateError,
}

/// <summary>
/// The signed licence payload. Every field is covered by the signature via canonical
/// serialisation, so editing any field invalidates the licence. Contains no private material.
/// </summary>
public sealed record LicensePayload(
    int FormatVersion,
    string ProductId,
    string LicenseId,
    string CustomerName,
    string MachineHash,
    DateTimeOffset IssuedUtc,
    LicenseType Type,
    string? Notes,
    string? Edition,
    string SignatureAlgorithm);

/// <summary>A licence document: the payload plus its detached signature.</summary>
public sealed record LicenseDocument(LicensePayload Payload, byte[] Signature);

/// <summary>The machine identity: a hash and a short human-copyable installation code.</summary>
public sealed record MachineFingerprint(int Version, string Hash, string InstallationCode);

/// <summary>Result of a licence verification attempt.</summary>
public sealed record LicenseVerification(LicenseStatus Status, LicensePayload? Payload, string Code)
{
    public bool IsValid => Status == LicenseStatus.Licensed;
}
