// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Security;

/// <summary>Hashes and verifies machine-to-machine client secrets. Never compare plaintext secrets directly.</summary>
public interface IClientSecretHasher
{
    /// <summary>Produces a hash of the given plaintext secret suitable for storage.</summary>
    string Hash(string plaintextSecret);

    /// <summary>Verifies a plaintext secret against a previously produced hash using a constant-time comparison.</summary>
    bool Verify(string plaintextSecret, string hashedSecret);
}
