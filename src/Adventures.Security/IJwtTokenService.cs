using System.Security.Claims;

namespace Adventures.Security;

/// <summary>Result of issuing a JWT: the encoded token plus its absolute expiry (UTC).</summary>
public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>Issues signed JWTs for authenticated principals.</summary>
public interface IJwtTokenService
{
    IssuedToken IssueToken(string subject, IEnumerable<Claim>? additionalClaims = null);

    /// <summary>
    /// Issues a token for a machine-to-machine client subject, embedding the granted scopes as a
    /// single space-delimited "scope" claim (standard OAuth2 convention).
    /// </summary>
    IssuedToken IssueClientToken(string clientId, IEnumerable<string> scopes);
}
