using Adventures.Security;
using Xunit;

namespace Adventures.Security.Tests;

public class ClientSecretHasherTests
{
    private readonly ClientSecretHasher _hasher = new();

    [Fact]
    public void Hash_ProducesDifferentOutputForSameSecret_DueToRandomSalt()
    {
        var first = _hasher.Hash("super-secret");
        var second = _hasher.Hash("super-secret");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForMatchingSecretAndHash()
    {
        var hashed = _hasher.Hash("correct-secret");

        Assert.True(_hasher.Verify("correct-secret", hashed));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForMismatchedSecret()
    {
        var hashed = _hasher.Hash("correct-secret");

        Assert.False(_hasher.Verify("wrong-secret", hashed));
    }

    [Theory]
    [InlineData("", "salt:hash")]
    [InlineData("secret", "")]
    [InlineData("secret", "not-a-valid-format")]
    [InlineData("secret", "bm90YmFzZTY0:!!!notbase64!!!")]
    public void Verify_ReturnsFalse_ForMalformedOrEmptyInput(string plaintext, string hashed)
    {
        Assert.False(_hasher.Verify(plaintext, hashed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_ThrowsForNullOrEmptySecret(string? secret)
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.Hash(secret!));
    }
}
