// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Security.Tests;

public class ScopeAuthorizationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddScopeAuthorization_RegistersScopeAuthorizationHandler()
    {
        var services = new ServiceCollection();

        services.AddScopeAuthorization("mcp.postgres.query");
        var provider = services.BuildServiceProvider();

        var handlers = provider.GetServices<IAuthorizationHandler>();
        Assert.Contains(handlers, h => h is ScopeAuthorizationHandler);
    }

    [Fact]
    public void AddScopeAuthorization_RegistersAPolicyPerScope()
    {
        var services = new ServiceCollection();

        services.AddScopeAuthorization("mcp.postgres.query", "mcp.filesearch.search");
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.GetPolicy(JwtServiceCollectionExtensions.ScopePolicyName("mcp.postgres.query")));
        Assert.NotNull(options.GetPolicy(JwtServiceCollectionExtensions.ScopePolicyName("mcp.filesearch.search")));
    }

    [Fact]
    public void ScopePolicyName_FollowsNamingConvention()
    {
        Assert.Equal("Scope:mcp.postgres.query", JwtServiceCollectionExtensions.ScopePolicyName("mcp.postgres.query"));
    }
}
