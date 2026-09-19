using System.Security.Cryptography;
using System.Text;

namespace Adventures.Security;

/// <summary>
/// Hashes client secrets using a random per-secret salt + SHA-256, stored as "{saltBase64}:{hashBase64}".
/// Client secrets are expected to be high-entropy generated values (not user-chosen passwords), so a
/// single salted SHA-256 round is an appropriate trade-off here; a user-facing password store should
/// use a slower KDF (PBKDF2/Argon2) instead.
/// </summary>
public sealed class ClientSecretHasher : IClientSecretHasher
{
    private const int SaltSizeBytes = 16;

    public string Hash(string plaintextSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextSecret);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = ComputeHash(salt, plaintextSecret);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string plaintextSecret, string hashedSecret)
    {
        if (string.IsNullOrEmpty(plaintextSecret) || string.IsNullOrEmpty(hashedSecret))
        {
            return false;
        }

        var parts = hashedSecret.Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expectedHash = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = ComputeHash(salt, plaintextSecret);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] ComputeHash(byte[] salt, string plaintextSecret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(plaintextSecret);
        var combined = new byte[salt.Length + secretBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(secretBytes, 0, combined, salt.Length, secretBytes.Length);
        return SHA256.HashData(combined);
    }
}
