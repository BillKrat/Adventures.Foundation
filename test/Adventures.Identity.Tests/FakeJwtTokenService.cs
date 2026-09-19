using System.Security.Claims;
using Adventures.Security;

namespace Adventures.Identity.Tests;

internal sealed class FakeJwtTokenService : IJwtTokenService
{
    public IssuedToken TokenToReturn { get; set; } = new("fake-token", DateTimeOffset.UtcNow.AddMinutes(30));
    public List<(string Subject, IReadOnlyList<Claim> Claims)> IssueTokenCalls { get; } = [];

    public IssuedToken IssueToken(string subject, IEnumerable<Claim>? additionalClaims = null)
    {
        IssueTokenCalls.Add((subject, (additionalClaims ?? []).ToArray()));
        return TokenToReturn;
    }

    public IssuedToken IssueClientToken(string clientId, IEnumerable<string> scopes) => throw new NotSupportedException();
}
