namespace Adventures.Security;

/// <summary>Hashes and verifies user-facing passwords. Never compare plaintext passwords directly.</summary>
public interface IPasswordHasher
{
    /// <summary>Produces a hash of the given plaintext password suitable for storage.</summary>
    string Hash(string plaintextPassword);

    /// <summary>Verifies a plaintext password against a previously produced hash using a constant-time comparison.</summary>
    bool Verify(string plaintextPassword, string hashedPassword);
}
