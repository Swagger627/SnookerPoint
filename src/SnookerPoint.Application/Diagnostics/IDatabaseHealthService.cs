using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Diagnostics;

/// <summary>Status of one managed data folder.</summary>
public sealed record FolderStatus(string Name, string Path, bool Exists, int FileCount, long SizeBytes);

/// <summary>A read-only snapshot of database and environment health for the admin area.</summary>
public sealed record DatabaseHealth(
    string DatabaseLocation,
    long DatabaseSizeBytes,
    string SchemaVersion,
    DateTimeOffset? LastBackupUtc,
    DateTimeOffset? LastBackupFailureUtc,
    string IntegrityStatus,
    IReadOnlyList<FolderStatus> Folders,
    long AvailableDiskBytes,
    string AppVersion);

/// <summary>
/// Read-only database and environment diagnostics for authorised administrators. Never
/// exposes raw SQL, a data-editing surface, or any secret (passwords, PINs, recovery codes).
/// </summary>
public interface IDatabaseHealthService
{
    /// <summary>A health snapshot. Does not run the (heavier) integrity check.</summary>
    DatabaseHealth GetHealth();

    /// <summary>Runs SQLite <c>PRAGMA integrity_check</c> and returns "ok" or the problem summary. Audited.</summary>
    OperationResult<string> RunIntegrityCheck(int actorUserId);

    /// <summary>Ensures the managed data folders exist and are writable. Audited.</summary>
    OperationResult<IReadOnlyList<FolderStatus>> ValidateManagedFolders(int actorUserId);

    /// <summary>Writes a plain-text diagnostic summary (no secrets) and returns its path. Audited.</summary>
    OperationResult<string> CreateDiagnosticSummary(string? destinationFolder, int actorUserId);

    /// <summary>
    /// Creates a support bundle (zip) with a sanitised summary and recent logs — no passwords,
    /// PINs, recovery codes, private keys, full licence text, raw machine identifiers, images,
    /// receipts or the business database. Returns the bundle path. Audited.
    /// </summary>
    OperationResult<string> CreateSupportBundle(string? destinationFolder, string? licensingStatusCode, int actorUserId);

    /// <summary>The managed logs and backups folders (for "open folder" actions).</summary>
    string LogsFolder { get; }

    string BackupsFolder { get; }
}
