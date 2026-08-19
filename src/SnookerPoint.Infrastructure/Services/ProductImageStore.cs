using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Common;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Copies product images into the managed <c>Images\Products</c> folder and resolves them
/// for display. The database stores only a managed relative path + hash + original name —
/// never a BLOB and never an absolute path. Validates type and size; a missing file never
/// throws (callers fall back to a placeholder).
/// </summary>
public sealed class ProductImageStore : IProductImageStore
{
    private const long MaxBytes = 8 * 1024 * 1024; // 8 MB
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    private readonly AppDataPaths _paths;
    private readonly ILogger<ProductImageStore> _logger;

    public ProductImageStore(AppDataPaths paths, ILogger<ProductImageStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public OperationResult<StoredImage> Save(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            return OperationResult<StoredImage>.Failure("That image file could not be found.");
        }

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            return OperationResult<StoredImage>.Failure("Please choose a PNG, JPG, JPEG or WebP image.");
        }

        var info = new FileInfo(sourceFilePath);
        if (info.Length == 0)
        {
            return OperationResult<StoredImage>.Failure("That image file is empty.");
        }

        if (info.Length > MaxBytes)
        {
            return OperationResult<StoredImage>.Failure("That image is too large. Please choose one under 8 MB.");
        }

        try
        {
            Directory.CreateDirectory(_paths.ProductImages);

            var bytes = File.ReadAllBytes(sourceFilePath);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(_paths.ProductImages, fileName);
            File.WriteAllBytes(fullPath, bytes);

            // Stored relative to the Images root, with forward slashes for portability.
            var relative = $"Products/{fileName}";
            return OperationResult<StoredImage>.Success(
                new StoredImage(relative, hash, Path.GetFileName(sourceFilePath)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving a product image failed.");
            return OperationResult<StoredImage>.Failure("The image could not be saved. Please try again.");
        }
    }

    public string? GetFullPath(string? relativePath)
    {
        var full = Resolve(relativePath);
        return full is not null && File.Exists(full) ? full : null;
    }

    public bool Exists(string? relativePath)
    {
        var full = Resolve(relativePath);
        return full is not null && File.Exists(full);
    }

    public void DeleteIfUnreferenced(string? relativePath, IEnumerable<string?> stillReferenced)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        if (stillReferenced.Any(p => string.Equals(p, relativePath, StringComparison.Ordinal)))
        {
            return; // another product still points at this file
        }

        try
        {
            var full = Resolve(relativePath);
            if (full is not null && File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete an unreferenced product image.");
        }
    }

    private string? Resolve(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_paths.Images, normalized);
    }
}
