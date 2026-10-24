// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Security;

namespace Adventures.Identity.Tests;

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public bool VerifyResult { get; set; } = true;
    public List<(string PlaintextPassword, string HashedPassword)> VerifyCalls { get; } = [];

    public string Hash(string plaintextPassword) => "hashed:" + plaintextPassword;

    public bool Verify(string plaintextPassword, string hashedPassword)
    {
        VerifyCalls.Add((plaintextPassword, hashedPassword));
        return VerifyResult;
    }
}
