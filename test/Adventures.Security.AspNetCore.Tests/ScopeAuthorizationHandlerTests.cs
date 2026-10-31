// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Adventures.Security.AspNetCore.Tests;

public class ScopeAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext CreateContext(ScopeRequirement requirement, params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        return new AuthorizationHandlerContext([requirement], principal, resource: null);
    }

    [Fact]
    public async Task HandleRequirementAsync_Succeeds_WhenScopeClaimContainsRequiredScope()
    {
        var handler = new ScopeAuthorizationHandler();
        var requirement = new ScopeRequirement("mcp.postgres.query");
        var context = CreateContext(requirement, new Claim("scope", "mcp.filesearch.search mcp.postgres.query"));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_Fails_WhenScopeClaimMissingRequiredScope()
    {
        var handler = new ScopeAuthorizationHandler();
        var requirement = new ScopeRequirement("mcp.postgres.query");
        var context = CreateContext(requirement, new Claim("scope", "mcp.filesearch.search"));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_Fails_WhenNoScopeClaimPresent()
    {
        var handler = new ScopeAuthorizationHandler();
        var requirement = new ScopeRequirement("mcp.postgres.query");
        var context = CreateContext(requirement);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_Succeeds_WhenMultipleScopeClaimsPresent()
    {
        var handler = new ScopeAuthorizationHandler();
        var requirement = new ScopeRequirement("mcp.postgres.query");
        var context = CreateContext(
            requirement,
            new Claim("scope", "mcp.filesearch.search"),
            new Claim("scope", "mcp.postgres.query"));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}
