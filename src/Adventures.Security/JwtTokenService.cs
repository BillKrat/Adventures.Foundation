using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Adventures.Security;

/// <summary>Issues HMAC-SHA256 signed JWTs based on the shared <see cref="JwtTokenOptions"/>.</summary>
public sealed class JwtTokenService(IOptions<JwtTokenOptions> options) : IJwtTokenService
{
    private readonly JwtTokenOptions _options = options.Value;

    public IssuedToken IssueToken(string subject, IEnumerable<Claim>? additionalClaims = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(_options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var encodedToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedToken(encodedToken, expires);
    }

    public IssuedToken IssueClientToken(string clientId, IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(scopes);

        var scopeClaimValue = string.Join(' ', scopes.Where(s => !string.IsNullOrWhiteSpace(s)));
        var claims = new[] { new Claim("scope", scopeClaimValue) };

        return IssueToken(clientId, claims);
    }
}
