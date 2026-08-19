using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class ProductImageStoreTests
{
    private static string WriteTempImage(string extension = ".png")
    {
        var path = Path.Combine(Path.GetTempPath(), $"snk-img-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        return path;
    }

    [Fact]
    public void Save_CopiesImage_AndReturnsPathAndHash()
    {
        using var env = new Phase1Environment();
        var source = WriteTempImage();
        try
        {
            var result = env.Images.Save(source);

            Assert.True(result.Succeeded, result.ErrorMessage);
            var stored = result.Value!;
            Assert.StartsWith("Products/", stored.RelativePath);
            Assert.False(string.IsNullOrWhiteSpace(stored.Hash));
            Assert.True(env.Images.Exists(stored.RelativePath));
            Assert.NotNull(env.Images.GetFullPath(stored.RelativePath));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void Save_RejectsUnsupportedType()
    {
        using var env = new Phase1Environment();
        var source = WriteTempImage(".txt");
        try
        {
            Assert.True(env.Images.Save(source).Failed);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void MissingImage_DoesNotThrow_AndResolvesToNull()
    {
        using var env = new Phase1Environment();

        Assert.Null(env.Images.GetFullPath("Products/does-not-exist.png"));
        Assert.False(env.Images.Exists("Products/does-not-exist.png"));
        Assert.Null(env.Images.GetFullPath(null));
    }

    [Fact]
    public void DeleteIfUnreferenced_KeepsFile_WhenStillReferenced()
    {
        using var env = new Phase1Environment();
        var source = WriteTempImage();
        var stored = env.Images.Save(source).Value!;
        File.Delete(source);

        // Another product still points at it → not deleted.
        env.Images.DeleteIfUnreferenced(stored.RelativePath, new[] { (string?)stored.RelativePath });
        Assert.True(env.Images.Exists(stored.RelativePath));

        // No references → deleted, no throw.
        env.Images.DeleteIfUnreferenced(stored.RelativePath, Array.Empty<string?>());
        Assert.False(env.Images.Exists(stored.RelativePath));
    }
}
