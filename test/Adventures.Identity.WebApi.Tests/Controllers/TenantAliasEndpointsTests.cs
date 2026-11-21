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
    [InlineData(nameof(TenantController.DeleteTenant))]
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

    [Fact]
    public async Task AddingOrRemovingAnAlias_ClearsTheTenantCaches_SoTheChangeWorksAtOnce()
    {
        var invalidator = new TenantCacheInvalidator();
        var controller = new TenantController(new FixedRequestTenant(new CurrentTenant(null, null, null, "h", TenantMatch.None)), invalidator);
        var aliases = new Aliases((_, host) => [host]);
        var before = invalidator.Token;

        await controller.AddAlias("t1", new AliasRequest("temp.example"), aliases, CancellationToken.None);
        Assert.True(before.HasChanged);

        var afterAdd = invalidator.Token;
        await controller.RemoveAlias("t1", "temp.example", aliases, CancellationToken.None);
        Assert.True(afterAdd.HasChanged);
    }

    private sealed class Remover(Func<string, string?, IReadOnlyCollection<string>, TenantRemoval?> remove) : ITenantRemover
    {
        public IReadOnlyCollection<string>? Protected { get; private set; }

        public Task<TenantRemoval?> RemoveAsync(string tenantId, string? confirmDomain, IReadOnlyCollection<string> protectedTenantIds, CancellationToken cancellationToken = default)
        {
            Protected = protectedTenantIds;
            return Task.FromResult(remove(tenantId, confirmDomain, protectedTenantIds));
        }
    }

    private sealed class HostResolver(string tenantId) : ITenantResolver
    {
        public Task<CurrentTenant> ResolveAsync(string? host, string? fallbackDomain = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CurrentTenant(tenantId, "Owner", "owner.test", host ?? string.Empty, TenantMatch.Domain));
    }

    [Fact]
    public async Task DeletingATenant_ProtectsTheTenantBeingWorkedInAndTheOneThatOwnsThisHost_AndClearsTheCaches()
    {
        var invalidator = new TenantCacheInvalidator();
        var controller = new TenantController(new FixedRequestTenant(new CurrentTenant("acting-id", "Acting", "acting.test", "www.owner.test", TenantMatch.Acting)), invalidator);
        var remover = new Remover((id, confirm, _) => new TenantRemoval(id, "Old", "old.test", []));
        var before = invalidator.Token;

        var result = await controller.DeleteTenant("old-id", "old.test", remover, new HostResolver("owner-id"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("acting-id", remover.Protected!);
        Assert.Contains("owner-id", remover.Protected!);
        Assert.True(before.HasChanged);
    }

    [Fact]
    public async Task DeletingAnUnknownTenantIsNotFound_AndARefusalIsA409WithItsReason()
    {
        var controller = Controller();

        Assert.IsType<NotFoundResult>(await controller.DeleteTenant("nope", "x.test", new Remover((_, _, _) => null), new HostResolver("o"), CancellationToken.None));
        var refused = await controller.DeleteTenant("t1", "", new Remover((_, _, _) => throw new BusinessRuleException("Type its domain.")), new HostResolver("o"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        Assert.Equal("Type its domain.", conflict.Value);
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
