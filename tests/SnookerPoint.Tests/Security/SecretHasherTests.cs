using SnookerPoint.Infrastructure.Security;

namespace SnookerPoint.Tests.Security;

public class SecretHasherTests
{
    private readonly Pbkdf2SecretHasher _hasher = new(iterations: 10_000);

    [Fact]
    public void Hash_IsNotPlaintext_AndIsSalted()
    {
        var a = _hasher.Hash("secret123");
        var b = _hasher.Hash("secret123");

        Assert.DoesNotContain("secret123", a);
        Assert.NotEqual(a, b); // different salts → different encoded hashes
    }

    [Fact]
    public void Verify_CorrectSecret_ReturnsValid()
    {
        var hash = _hasher.Hash("secret123");

        var result = _hasher.Verify("secret123", hash);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsInvalid()
    {
        var hash = _hasher.Hash("secret123");

        Assert.False(_hasher.Verify("wrong", hash).IsValid);
    }

    [Fact]
    public void Verify_PinRoundTrips()
    {
        var hash = _hasher.Hash("1234");

        Assert.True(_hasher.Verify("1234", hash).IsValid);
        Assert.False(_hasher.Verify("4321", hash).IsValid);
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsInvalid()
    {
        Assert.False(_hasher.Verify("secret123", "not-a-valid-hash").IsValid);
    }

    [Fact]
    public void Verify_FlagsRehash_WhenIterationsIncrease()
    {
        var weak = new Pbkdf2SecretHasher(iterations: 10_000).Hash("secret123");
        var stronger = new Pbkdf2SecretHasher(iterations: 50_000);

        var result = stronger.Verify("secret123", weak);

        Assert.True(result.IsValid);
        Assert.True(result.NeedsRehash);
    }
}
