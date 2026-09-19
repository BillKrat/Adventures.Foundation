using System.Security.Cryptography;

namespace Adventures.Security;

/// <summary>
/// Hashes user-facing passwords using PBKDF2-HMAC-SHA256 with a random per-password salt, stored as
/// "{iterations}:{saltBase64}:{hashBase64}". Unlike <see cref="ClientSecretHasher"/> (appropriate for
/// high-entropy generated M2M secrets), user-chosen passwords are low-entropy by nature and require a
/// deliberately slow, iterated KDF to resist offline brute-force/dictionary attacks - this is
/// non-negotiable: security takes priority over convenience for anything protecting a human's
/// credential, so a single fast hash round (as used for M2M secrets) must never be reused here.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000; // OWASP (2023) minimum recommendation for PBKDF2-HMAC-SHA256.

    public string Hash(string plaintextPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextPassword);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = ComputeHash(salt, plaintextPassword, Iterations);

        return $"{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string plaintextPassword, string hashedPassword)
    {
        if (string.IsNullOrEmpty(plaintextPassword) || string.IsNullOrEmpty(hashedPassword))
        {
            return false;
        }

        var parts = hashedPassword.Split(':', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = ComputeHash(salt, plaintextPassword, iterations);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] ComputeHash(byte[] salt, string plaintextPassword, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(plaintextPassword, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);
}
