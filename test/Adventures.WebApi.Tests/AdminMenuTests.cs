using System.Security.Claims;
using Adventures.Security;
using Xunit;

namespace Adventures.WebApi.Tests;

public sealed class AdminMenuTests
{
    private static readonly AdminMenuItemDefinition[] Definitions =
    [
        new("Content", "posts", "Posts", "/admin/posts", "article", "TenantAdmin", Available: false, Order: 1),
        new("Users", "roles", "Roles", "/admin/roles", "badge", "TenantAdmin", Order: 2),
        new("Users", "users", "Users", "/admin/users", "group", "TenantAdmin", Order: 1),
        new("System", "exceptions", "Exceptions", "/admin/exceptions", "warning", "TenantAdmin", Order: 1),
        new("Account", "profile", "Profile", "/profile", "person", RequiredRole: null, Order: 1),
    ];

    private static ClaimsPrincipal Caller(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Length == 0 ? [] : [new Claim(WellKnownClaims.Roles, string.Join(' ', roles))],
            authenticationType: "Test"));

    [Fact]
    public void AnAdministrator_SeesEverySection_InTheDeclaredOrder_WithUnbuiltScreensListedWithoutARoute()
    {
        var model = AdminMenuBuilder.Build(Definitions, Caller("TenantAdmin"));

        Assert.Equal(["Content", "Users", "System", "Account"], model.Sections.Select(s => s.Id));
        Assert.Equal(["users", "roles"], model.Sections.Single(s => s.Id == "Users").Items.Select(i => i.Id));
        var posts = model.Sections.Single(s => s.Id == "Content").Items.Single();
        Assert.False(posts.Available);
        Assert.Null(posts.Route);
        Assert.Equal(1, model.ContractVersion);
    }

    [Fact]
    public void ACallerWithoutTheRole_SeesOnlyWhatNeedsNoRole_AndAnAnonymousCallerSeesNothing()
    {
        var plain = AdminMenuBuilder.Build(Definitions, Caller());
        Assert.Equal(["profile"], plain.Sections.SelectMany(s => s.Items).Select(i => i.Id));

        var anonymous = AdminMenuBuilder.Build(Definitions, new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.True(anonymous.IsEmpty);
    }

    [Fact]
    public void RolesComeFromEitherClaimSpelling_AndBadgesShowOnlyWhenPositive()
    {
        var viaClaimTypesRole = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"));

        var model = AdminMenuBuilder.Build(Definitions, viaClaimTypesRole, new Dictionary<string, int> { ["exceptions"] = 3, ["users"] = 0 });

        Assert.Equal(3, model.Sections.SelectMany(s => s.Items).Single(i => i.Id == "exceptions").Badge);
        Assert.Null(model.Sections.SelectMany(s => s.Items).Single(i => i.Id == "users").Badge);
    }
}
