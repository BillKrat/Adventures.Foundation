using Microsoft.Extensions.Configuration;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// The domain is one setting (<c>Site:Domain</c>). Set, it decides the allowed browser origins, the sign-in tenant and the addresses of the API and MCP sites;
/// unset, the explicit settings (our own defaults) apply, so an existing installation does not change. Threat addressed: a leftover origin or tenant from the
/// project's own domain staying trusted in somebody else's installation, and a malformed value turning into an allowed origin.
/// </summary>
public sealed class SiteDomainTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void WithoutADomain_TheExplicitSettingsApplyUnchanged()
    {
        var config = Config(("Cors:AllowedOrigins:0", "https://one.example"), ("McpServer:BaseUrl", "https://mcp.one.example/"), ("Auth:Tenant", "one.example"));

        Assert.Null(SiteDomain.Domain(config));
        Assert.Equal(["https://one.example"], SiteDomain.AllowedOrigins(config, isDevelopment: false));
        Assert.Equal("https://mcp.one.example/", SiteDomain.McpBaseUrl(config));
        Assert.Equal("one.example", SiteDomain.Tenant(config, "fallback.example"));
    }

    [Fact]
    public void WithADomain_ItDecidesEverything_AndLeftoverExplicitSettingsAreIgnored()
    {
        var config = Config(
            ("Site:Domain", "Mine.Example"), ("Cors:AllowedOrigins:0", "https://theirs.example"), ("Cors:AllowedOrigins:5", "https://also-theirs.example"),
            ("McpServer:BaseUrl", "https://mcp.theirs.example/"), ("Auth:Tenant", "theirs.example"));

        Assert.Equal("mine.example", SiteDomain.Domain(config));
        Assert.Equal(["https://mine.example", "https://www.mine.example"], SiteDomain.AllowedOrigins(config, isDevelopment: false));
        Assert.Equal("https://mcp.mine.example/", SiteDomain.McpBaseUrl(config));
        Assert.Equal("https://api.mine.example/", SiteDomain.ApiBaseUrl(config));
        Assert.Equal("mine.example", SiteDomain.Tenant(config, "fallback.example"));
    }

    [Fact]
    public void LocalhostOriginsAreAllowedOnlyInDevelopment()
    {
        var config = Config(("Site:Domain", "mine.example"));

        Assert.Contains("http://localhost:4200", SiteDomain.AllowedOrigins(config, isDevelopment: true));
        Assert.DoesNotContain("http://localhost:4200", SiteDomain.AllowedOrigins(config, isDevelopment: false));
    }

    [Theory]
    [InlineData("https://mine.example")]
    [InlineData("mine.example/path")]
    [InlineData("mine example")]
    [InlineData("-bad.example")]
    [InlineData("localhost")]
    [InlineData("mine.example:8080")]
    [InlineData("*.example")]
    public void AMalformedDomainStopsTheStartup_ItNeverBecomesAnAllowedOrigin(string value)
    {
        var config = Config(("Site:Domain", value));

        Assert.Throws<InvalidOperationException>(() => SiteDomain.Domain(config));
    }

    [Fact]
    public void ABlankDomainMeansNotSet()
    {
        Assert.Null(SiteDomain.Domain(Config(("Site:Domain", "  "))));
        Assert.Equal("fallback.example", SiteDomain.Tenant(Config(), "fallback.example"));
        Assert.Equal("https://api.fallback.example/", SiteDomain.ApiBaseUrl(Config(), "https://api.fallback.example/"));
    }
}
