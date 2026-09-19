using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Adventures.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.Security.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(JwtTokenOptions? options = null)
    {
        options ??= new JwtTokenOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "0123456789abcdef0123456789abcdef", // 32+ chars
            AccessTokenLifetime = TimeSpan.FromMinutes(30),
        };

        return new JwtTokenService(Options.Create(options));
    }

    [Fact]
    public void IssueToken_ProducesTokenWithExpectedIssuerAudienceAndSubject()
    {
        var service = CreateService();

        var issued = service.IssueToken("alice");

        Assert.False(string.IsNullOrWhiteSpace(issued.AccessToken));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        Assert.Equal("TestIssuer", jwt.Issuer);
        Assert.Contains("TestAudience", jwt.Audiences);
        Assert.Equal("alice", jwt.Subject);
    }

    [Fact]
    public void IssueToken_SetsExpiryBasedOnConfiguredLifetime()
    {
        var options = new JwtTokenOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "0123456789abcdef0123456789abcdef",
            AccessTokenLifetime = TimeSpan.FromMinutes(5),
        };
        var service = CreateService(options);

        var before = DateTimeOffset.UtcNow;
        var issued = service.IssueToken("bob");
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(issued.ExpiresAtUtc, before.Add(options.AccessTokenLifetime), after.Add(options.AccessTokenLifetime));
    }

    [Fact]
    public void IssueToken_IncludesAdditionalClaims()
    {
        var service = CreateService();
        var claims = new[] { new Claim(ClaimTypes.Name, "carol"), new Claim("role", "admin") };

        var issued = service.IssueToken("carol", claims);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);

        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Name && c.Value == "carol");
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "admin");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IssueToken_ThrowsForEmptyOrWhitespaceSubject(string subject)
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.IssueToken(subject));
    }

    [Fact]
    public void IssueToken_ThrowsForNullSubject()
    {
        var service = CreateService();

        Assert.Throws<ArgumentNullException>(() => service.IssueToken(null!));
    }

    [Fact]
    public void IssueToken_GeneratesUniqueJtiPerCall()
    {
        var service = CreateService();

        var first = new JwtSecurityTokenHandler().ReadJwtToken(service.IssueToken("dave").AccessToken);
        var second = new JwtSecurityTokenHandler().ReadJwtToken(service.IssueToken("dave").AccessToken);

        var firstJti = first.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var secondJti = second.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        Assert.NotEqual(firstJti, secondJti);
    }

    [Fact]
    public void IssueClientToken_SetsSubjectToClientIdAndScopeClaimSpaceDelimited()
    {
        var service = CreateService();

        var issued = service.IssueClientToken("mcp-host", ["mcp.postgres.query", "mcp.filesearch.search"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);

        Assert.Equal("mcp-host", jwt.Subject);
        var scopeClaim = jwt.Claims.Single(c => c.Type == "scope").Value;
        Assert.Equal("mcp.postgres.query mcp.filesearch.search", scopeClaim);
    }

    [Fact]
    public void IssueClientToken_FiltersOutEmptyOrWhitespaceScopes()
    {
        var service = CreateService();

        var issued = service.IssueClientToken("mcp-host", ["mcp.postgres.query", "", "   ", "mcp.filesearch.search"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);

        var scopeClaim = jwt.Claims.Single(c => c.Type == "scope").Value;
        Assert.Equal("mcp.postgres.query mcp.filesearch.search", scopeClaim);
    }

    [Fact]
    public void IssueClientToken_ThrowsForNullOrEmptyClientId()
    {
        var service = CreateService();

        Assert.ThrowsAny<ArgumentException>(() => service.IssueClientToken("", ["mcp.postgres.query"]));
    }

    [Fact]
    public void IssueClientToken_ThrowsForNullScopes()
    {
        var service = CreateService();

        Assert.Throws<ArgumentNullException>(() => service.IssueClientToken("mcp-host", null!));
    }
}
