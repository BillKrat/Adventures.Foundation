namespace Adventures.Security;

/// <summary>Hashes and verifies machine-to-machine client secrets. Never compare plaintext secrets directly.</summary>
public interface IClientSecretHasher
{
    /// <summary>Produces a hash of the given plaintext secret suitable for storage.</summary>
    string Hash(string plaintextSecret);

    /// <summary>Verifies a plaintext secret against a previously produced hash using a constant-time comparison.</summary>
    bool Verify(string plaintextSecret, string hashedSecret);
}
