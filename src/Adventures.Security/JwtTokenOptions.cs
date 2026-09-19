namespace Adventures.Security;

/// <summary>
/// Configuration options for issuing and validating JWTs, bound from the "Jwt" configuration section.
/// The same values must be shared by any application/service that needs to issue or validate tokens.
/// </summary>
public sealed class JwtTokenOptions
{
    public const string SectionName = "Jwt";

    /// <summary>The token issuer ("iss" claim), e.g. "YourApp.WebApi".</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The intended token audience ("aud" claim), e.g. "YourApp.Clients".</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key (HMAC-SHA256). Must be at least 32 bytes/256 bits.
    /// Store this in user-secrets locally and in an environment variable/secret store in deployed environments -
    /// never commit it to source control.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long issued access tokens remain valid.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);
}
