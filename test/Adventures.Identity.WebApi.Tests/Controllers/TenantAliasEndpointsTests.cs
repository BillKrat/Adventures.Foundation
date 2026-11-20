using Adventures.Identity.WebApi;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.WebApi;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>Host aliases are the platform administrator's: every route that reads or changes which host reaches which tenant needs the platform right, and a refused name is a 409.</summary>
public sealed class TenantAliasEndpointsTests
{
    private sealed class Aliases(Func<string, string, IReadOnlyList<string>?> add) : ITenantAliasEditor
    {
        public Task<IReadOnlyList<TenantHostInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantHostInfo>>([new("t1", "DEV Main", "global-webnet.com", ["temp.example"])]);

        public Task<IReadOnlyList<string>?> AddAsync(string tenantId, string host, CancellationToken cancellationToken = default) => Task.FromResult(add(tenantId, host));

        public Task<IReadOnlyList<string>?> RemoveAsync(string tenantId, string host, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>?>([]);
    }

    private static TenantController Controller() => new(new FixedRequestTenant(new CurrentTenant(null, null, null, "h", TenantMatch.None)));

    [Theory]
    [InlineData(nameof(TenantController.Hosts))]
    [InlineData(nameof(TenantController.AddAlias))]
    [InlineData(nameof(TenantController.RemoveAlias))]
    public void EveryAliasRouteNeedsThePlatformRight(string action)
    {
        var guard = Assert.Single(typeof(TenantController).GetMethod(action)!.GetCustomAttributes(typeof(RequireRightAttribute), inherit: true).Cast<RequireRightAttribute>());

        Assert.Equal(Rights.PlatformManage, guard.Right);
    }

    [Fact]
    public async Task AddingAnAliasAnswersTheTenantsAliases_AndAnUnknownTenantIsNotFound()
    {
        var aliases = new Aliases((id, host) => id == "t1" ? [host] : null);

        var ok = Assert.IsType<OkObjectResult>(await Controller().AddAlias("t1", new AliasRequest("temp.example"), aliases, CancellationToken.None));
        Assert.Equal(["temp.example"], Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value));
        Assert.IsType<NotFoundResult>(await Controller().AddAlias("nope", new AliasRequest("temp.example"), aliases, CancellationToken.None));
    }

    private sealed class Acting(params TenantHostInfo[] allowed) : IActingTenants
    {
        public string? AskedFor;

        public Task<IReadOnlyList<TenantHostInfo>> ForAsync(string userName, CancellationToken cancellationToken = default)
        {
            AskedFor = userName;
            return Task.FromResult<IReadOnlyList<TenantHostInfo>>(allowed);
        }

        public Task<TenantHostInfo?> AllowedAsync(string userName, string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(allowed.FirstOrDefault(t => t.Id == tenantId));
    }

    [Fact]
    public async Task TheSwitcherListIsTheSignedInPersonsTenants_WithTheOneNowActedIn()
    {
        var controller = new TenantController(new FixedRequestTenant(new CurrentTenant("t2", "Two", "two.test", "h", TenantMatch.Acting)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Admin")], "Test")) },
        };
        var acting = new Acting(new TenantHostInfo("t1", "One", "one.test", []), new TenantHostInfo("t2", "Two", "two.test", []));

        var ok = Assert.IsType<OkObjectResult>(await controller.Mine(acting, CancellationToken.None));

        Assert.Equal("Admin", acting.AskedFor);
        Assert.Equal("t2", ok.Value!.GetType().GetProperty("ActingId")!.GetValue(ok.Value));
        Assert.Equal(2, ((IReadOnlyList<TenantHostInfo>)ok.Value.GetType().GetProperty("Tenants")!.GetValue(ok.Value)!).Count);
    }

    [Fact]
    public void TheSwitcherListNeedsASignedInPerson_NotAnyParticularRight()
    {
        Assert.Empty(typeof(TenantController).GetMethod(nameof(TenantController.Mine))!.GetCustomAttributes(typeof(RequireRightAttribute), inherit: true));
        Assert.NotEmpty(typeof(TenantController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true));
    }

    [Fact]
    public async Task ARefusedHostNameIsAConflictWithTheReason()
    {
        var aliases = new Aliases((_, _) => throw new BusinessRuleException("already reaches another tenant"));

        var result = await Controller().AddAlias("t1", new AliasRequest("taken.example"), aliases, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("already reaches another tenant", conflict.Value);
    }
}
