using Adventures.Identity.WebApi;
using System.Security.Claims;
using Adventures.Entities;
using Adventures.WebApi;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// A platform administrator can find a person of another tenant on the Users screen (to give them a role in the tenant being worked in): everyone when opening a person, and in the
/// list only when asked. Nobody else is ever widened. Threat addressed: cross-tenant reads limited to the platform right.
/// </summary>
public sealed class UsersVisibilityTests
{
    private sealed class Caller(bool platform) : IRightsAuthorizer
    {
        public Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default) => Task.FromResult(platform && right == Rights.PlatformManage);
    }

    private static async Task<bool> SeesEveryoneAsync(bool platform, string action, string query)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin")], "Test")) };
        http.Request.QueryString = new QueryString(query);
        var descriptor = new ActionDescriptor();
        descriptor.RouteValues["action"] = action;
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var visibility = new UserVisibility();

        await new PlatformUserVisibilityFilter(new Caller(platform), visibility).OnActionExecutionAsync(
            new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object()),
            () => Task.FromResult(new ActionExecutedContext(actionContext, [], new object())));
        return visibility.SeeEveryone;
    }

    [Fact]
    public async Task APlatformAdministratorSeesEveryoneWhenOpeningAPerson_AndInTheListOnlyWhenAsking()
    {
        Assert.True(await SeesEveryoneAsync(true, nameof(UsersController.Get), string.Empty));
        Assert.False(await SeesEveryoneAsync(true, nameof(UsersController.List), string.Empty));
        Assert.True(await SeesEveryoneAsync(true, nameof(UsersController.List), "?filter=Scope&filterValue=Everyone"));
        Assert.False(await SeesEveryoneAsync(true, nameof(UsersController.List), "?filter=Scope&filterValue=Mine"));
    }

    [Fact]
    public async Task ANonPlatformAdministratorIsNeverWidened_EvenWhenAsking()
    {
        Assert.False(await SeesEveryoneAsync(false, nameof(UsersController.Get), string.Empty));
        Assert.False(await SeesEveryoneAsync(false, nameof(UsersController.List), "?filter=Scope&filterValue=Everyone"));
    }

    [Fact]
    public async Task APersonOutsideTheTenantIsReachableOnlyWhenEveryoneIsVisible()
    {
        var outsider = new FakeMembership { Members = ["user-bill"] };
        var switches = new NoSwitches();

        var hidden = UsersControllerTests.BuildController(new FakeUserPresenter(), "Admin", membership: outsider);
        Assert.IsType<NotFoundResult>(await hidden.GetPlatform("user-claude", switches, new Caller(true), CancellationToken.None));

        var shown = UsersControllerTests.BuildController(new FakeUserPresenter(), "Admin", membership: outsider, visibility: new UserVisibility { SeeEveryone = true });
        Assert.IsType<OkObjectResult>(await shown.GetPlatform("user-claude", switches, new Caller(true), CancellationToken.None));
    }

    private sealed class NoSwitches : IPlatformAdministrators
    {
        public Task<bool> IsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlySet<string>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task SetAsync(string userId, bool on, string changedBy, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void TheUsersControllerRunsTheVisibilityFilter_AndTheFilterIsRegistered()
    {
        var filter = Assert.Single(typeof(UsersController).GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true).Cast<ServiceFilterAttribute>());

        Assert.Equal(typeof(PlatformUserVisibilityFilter), filter.ServiceType);
    }
}
