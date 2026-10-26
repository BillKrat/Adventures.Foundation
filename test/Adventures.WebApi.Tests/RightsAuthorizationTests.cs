using System.Security.Claims;
using System.Reflection;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// S2: routes are guarded by rights, never role names. A caller needs the right a route names; with none the route answers 403 (401 when
/// not signed in). An unnamed right for a verb is a refusal, so a route cannot be left open by forgetting a name. A later entitlement
/// (a plan that includes a feature) plugs in as a condition without touching the routes.
/// </summary>
public sealed class RightsAuthorizationTests
{
    private sealed class FakeResolver(Dictionary<string, string[]> byUser) : IRightsResolver
    {
        public int Calls { get; private set; }

        public Task<IReadOnlySet<string>> ResolveIfRoleAsync(string userName, string roleId, string roleRights, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<string>> ResolveAsync(string userName, IEnumerable<string> signInRoles, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (signInRoles.Contains("TenantAdmin"))
            {
                return Task.FromResult(Rights.AllNames);
            }

            return Task.FromResult<IReadOnlySet<string>>(byUser.TryGetValue(userName, out var rights) ? Rights.Expand(rights) : new HashSet<string>());
        }
    }

    private sealed class Veto(string right) : IRightCondition
    {
        public Task<bool> AllowsAsync(ClaimsPrincipal user, string requested, CancellationToken cancellationToken = default) => Task.FromResult(requested != right);
    }

    private static readonly Dictionary<string, string[]> People = new()
    {
        ["editor"] = [Rights.PostsRead, Rights.PostsCreate, Rights.PostsEditOwn],
        ["reviewer"] = [Rights.PostsRead, Rights.CollaborationReview],
    };

    private static ClaimsPrincipal Person(string? name, string? roles = null)
    {
        if (name is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (roles is not null)
        {
            claims.Add(new Claim(WellKnownClaims.Roles, roles));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static (RightsAuthorizer Authorizer, FakeResolver Resolver) Authorizer(params IRightCondition[] conditions)
    {
        var resolver = new FakeResolver(People);
        return (new RightsAuthorizer(resolver, conditions), resolver);
    }

    private static AuthorizationFilterContext ContextFor(ClaimsPrincipal user, string method, IRightsAuthorizer authorizer, MethodInfo? action = null)
    {
        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = new ServiceCollection().AddSingleton(authorizer).BuildServiceProvider(),
        };
        http.Request.Method = method;
        var descriptor = new ControllerActionDescriptor { MethodInfo = action ?? typeof(RightsAuthorizationTests).GetMethod(nameof(NoAttributeAction), BindingFlags.NonPublic | BindingFlags.Static)! };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
    }

    private static void NoAttributeAction() { }

    [RequireRight(Rights.PostsPublish)]
    private static void PublishAction() { }

    // ---- the authorizer

    [Fact]
    public async Task ThePersonsRightsComeFromTheirRoles_AndTheLegacyAdministratorHasEveryRight()
    {
        var (authorizer, _) = Authorizer();

        Assert.True(await authorizer.IsAllowedAsync(Person("editor"), Rights.PostsCreate));
        Assert.False(await authorizer.IsAllowedAsync(Person("editor"), Rights.PostsEditAny));
        Assert.True(await authorizer.IsAllowedAsync(Person("anyone", "TenantAdmin"), Rights.RolesManage));
        Assert.False(await authorizer.IsAllowedAsync(Person(null), Rights.PostsRead));
    }

    [Fact]
    public async Task OneRequestResolvesTheRightsOnce_NotOncePerCheck()
    {
        var (authorizer, resolver) = Authorizer();
        var editor = Person("editor");

        await authorizer.IsAllowedAsync(editor, Rights.PostsRead);
        await authorizer.IsAllowedAsync(editor, Rights.PostsCreate);
        await authorizer.IsAllowedAsync(editor, Rights.PostsPublish);

        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task AConditionCanRefuseARightThePersonHolds_TheSeamForEntitlementsLater()
    {
        var (authorizer, _) = Authorizer(new Veto(Rights.DiagnosticsAi));

        Assert.True(await authorizer.IsAllowedAsync(Person("x", "TenantAdmin"), Rights.PostsRead));
        Assert.False(await authorizer.IsAllowedAsync(Person("x", "TenantAdmin"), Rights.DiagnosticsAi));
    }

    // ---- the route filters

    [Fact]
    public async Task ARouteNeedsItsRight_401WhenNotSignedIn_403WhenSignedInWithout_OpenWithIt()
    {
        var (authorizer, _) = Authorizer();
        var filter = new RequireRightAttribute(Rights.PostsPublish);

        var anonymous = ContextFor(Person(null), "POST", authorizer);
        await filter.OnAuthorizationAsync(anonymous);
        Assert.IsType<UnauthorizedResult>(anonymous.Result);

        var without = ContextFor(Person("editor"), "POST", authorizer);
        await filter.OnAuthorizationAsync(without);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(without.Result).StatusCode);

        var allowed = ContextFor(Person("anyone", "TenantAdmin"), "POST", authorizer);
        await filter.OnAuthorizationAsync(allowed);
        Assert.Null(allowed.Result);
    }

    [Theory]
    [InlineData("GET", Rights.PostsRead)]
    [InlineData("HEAD", Rights.PostsRead)]
    [InlineData("POST", Rights.PostsCreate)]
    [InlineData("PUT", Rights.PostsEditAny)]
    [InlineData("PATCH", Rights.PostsEditAny)]
    [InlineData("DELETE", Rights.PostsDeleteAny)]
    public async Task AnEntityControllerNamesOneRightPerVerb(string method, string right)
    {
        var (authorizer, _) = Authorizer();
        var filter = new AdminRightsAttribute { Read = Rights.PostsRead, Create = Rights.PostsCreate, Update = Rights.PostsEditAny, Delete = Rights.PostsDeleteAny };
        var holder = Person("holder");
        var people = new Dictionary<string, string[]> { ["holder"] = [right] };
        var scoped = new RightsAuthorizer(new FakeResolver(people), []);

        var allowed = ContextFor(holder, method, scoped);
        await filter.OnAuthorizationAsync(allowed);
        Assert.Null(allowed.Result);

        var other = ContextFor(Person("nobody"), method, authorizer);
        await filter.OnAuthorizationAsync(other);
        Assert.NotNull(other.Result);
    }

    [Fact]
    public async Task AVerbWithNoNamedRightIsRefused_EvenForSomeoneWithManyRights_SoARouteCannotBeLeftOpenByForgettingAName()
    {
        var (authorizer, _) = Authorizer();
        var filter = new AdminRightsAttribute { Read = Rights.PostsRead };

        var context = ContextFor(Person("editor"), "DELETE", authorizer);
        await filter.OnAuthorizationAsync(context);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task AnActionsOwnRightReplacesTheControllersVerbRights_SoPublishNeedsPublishAndNotEditing()
    {
        var holder = new RightsAuthorizer(new FakeResolver(new() { ["publisher"] = [Rights.PostsPublish] }), []);
        var controllerLevel = new AdminRightsAttribute { Read = Rights.PostsRead, Create = Rights.PostsCreate, Update = Rights.PostsEditAny, Delete = Rights.PostsDeleteAny };
        var publishAction = typeof(RightsAuthorizationTests).GetMethod(nameof(PublishAction), BindingFlags.NonPublic | BindingFlags.Static);

        var context = ContextFor(Person("publisher"), "POST", holder, publishAction);
        await controllerLevel.OnAuthorizationAsync(context);

        Assert.Null(context.Result); // the verb right (create) is skipped; the action's own filter decides
    }

    // ---- the menu

    private static readonly AdminMenuItemDefinition[] Items =
    [
        new("Content", "posts", "Posts", "/admin/posts", "article", RequiredRight: Rights.PostsRead, Order: 1),
        new("Users", "roles", "Roles", "/admin/roles", "badge", RequiredRight: Rights.RolesManage, Order: 1),
        new("System", "settings", "Settings", "/admin/settings", "settings", RequiredRight: Rights.SettingsManage, Order: 1),
    ];

    [Fact]
    public void TheMenuListsOnlyWhatTheCallersRightsAllow_AndNothingWithoutAdminAccess()
    {
        var withRights = new HashSet<string> { Rights.AdminAccess, Rights.PostsRead, Rights.SettingsManage };
        var model = AdminMenuBuilder.Build(Items, Person("editor"), badges: null, rights: withRights);
        Assert.Equal(["posts", "settings"], model.Sections.SelectMany(s => s.Items).Select(i => i.Id));

        var noAdminAccess = new HashSet<string> { Rights.PostsRead };
        Assert.True(AdminMenuBuilder.Build(Items, Person("editor"), badges: null, rights: noAdminAccess).IsEmpty);

        Assert.True(AdminMenuBuilder.Build(Items, Person("editor"), badges: null, rights: new HashSet<string>()).IsEmpty);
    }
}
