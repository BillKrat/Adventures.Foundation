using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Security.Tests;

public class RoleAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext CreateContext(RoleRequirement requirement, params Claim[] claims) =>
        new([requirement], new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")), resource: null);

    [Fact]
    public async Task Succeeds_WhenRolesClaimContainsRequiredRole()
    {
        var context = CreateContext(new RoleRequirement("TenantAdmin"), new Claim("roles", "Editor TenantAdmin"));

        await new RoleAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenRolesClaimLacksRequiredRole()
    {
        var context = CreateContext(new RoleRequirement("TenantAdmin"), new Claim("roles", "User"));

        await new RoleAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenNoRolesClaim()
    {
        var context = CreateContext(new RoleRequirement("TenantAdmin"));

        await new RoleAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenOnlyASubstringMatches()
    {
        var context = CreateContext(new RoleRequirement("Admin"), new Claim("roles", "TenantAdmin"));

        await new RoleAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AddRoleAuthorization_RegistersANamedPolicyThatEnforcesTheRole()
    {
        var services = new ServiceCollection().AddLogging().AddRoleAuthorization("TenantAdmin").BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", "TenantAdmin")], "Bearer"));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", "User")], "Bearer"));
        var policy = JwtServiceCollectionExtensions.RolePolicyName("TenantAdmin");

        Assert.True((await authorization.AuthorizeAsync(admin, null, policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
    }
}
