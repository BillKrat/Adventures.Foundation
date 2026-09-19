using Adventures.Security;
using Xunit;

namespace Adventures.Security.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesDifferentOutputForSamePassword_DueToRandomSalt()
    {
        var first = _hasher.Hash("Password");
        var second = _hasher.Hash("Password");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_EncodesIterationCountForFutureAlgorithmAgility()
    {
        var hashed = _hasher.Hash("Password");

        Assert.StartsWith("210000:", hashed);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForMatchingPasswordAndHash()
    {
        var hashed = _hasher.Hash("correct-password");

        Assert.True(_hasher.Verify("correct-password", hashed));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForMismatchedPassword()
    {
        var hashed = _hasher.Hash("correct-password");

        Assert.False(_hasher.Verify("wrong-password", hashed));
    }

    [Fact]
    public void Verify_SucceedsAgainstHashProducedWithDifferentIterationCount()
    {
        // Simulates verifying an older hash after Iterations was raised in a future release:
        // the iteration count is read from the stored hash itself, not hard-coded at verify time.
        const int legacyIterations = 1000;
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "legacy-password", salt, legacyIterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var legacyHash = $"{legacyIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";

        Assert.True(_hasher.Verify("legacy-password", legacyHash));
    }

    [Theory]
    [InlineData("", "1:salt:hash")]
    [InlineData("password", "")]
    [InlineData("password", "not-a-valid-format")]
    [InlineData("password", "not-a-number:c2FsdA==:aGFzaA==")]
    [InlineData("password", "1000:!!!notbase64!!!:aGFzaA==")]
    public void Verify_ReturnsFalse_ForMalformedOrEmptyInput(string plaintext, string hashed)
    {
        Assert.False(_hasher.Verify(plaintext, hashed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_ThrowsForNullOrEmptyPassword(string? password)
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.Hash(password!));
    }
}
