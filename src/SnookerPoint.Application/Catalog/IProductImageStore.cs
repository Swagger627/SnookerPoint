using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Catalog;

/// <summary>A stored image's managed relative path, content hash and original filename.</summary>
public sealed record StoredImage(string RelativePath, string Hash, string OriginalName);

/// <summary>
/// Copies product images into the managed app-data images folder and resolves them for
/// display. Images are stored as files (path + hash in the database), never as BLOBs. A
/// missing file never throws.
/// </summary>
public interface IProductImageStore
{
    /// <summary>Validates and copies a source image into the managed folder.</summary>
    OperationResult<StoredImage> Save(string sourceFilePath);

    /// <summary>Resolves a managed relative path to a full path, or null if not set/missing.</summary>
    string? GetFullPath(string? relativePath);

    /// <summary>True when the managed file for this relative path exists on disk.</summary>
    bool Exists(string? relativePath);

    /// <summary>
    /// Deletes the managed file for <paramref name="relativePath"/> only when no path in
    /// <paramref name="stillReferenced"/> points at the same file. Never throws.
    /// </summary>
    void DeleteIfUnreferenced(string? relativePath, IEnumerable<string?> stillReferenced);
}
