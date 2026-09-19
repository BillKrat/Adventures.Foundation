using Adventures.Security;

namespace Adventures.Identity.Tests;

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public bool VerifyResult { get; set; } = true;
    public List<(string PlaintextPassword, string HashedPassword)> VerifyCalls { get; } = [];

    public string Hash(string plaintextPassword) => throw new NotSupportedException();

    public bool Verify(string plaintextPassword, string hashedPassword)
    {
        VerifyCalls.Add((plaintextPassword, hashedPassword));
        return VerifyResult;
    }
}
