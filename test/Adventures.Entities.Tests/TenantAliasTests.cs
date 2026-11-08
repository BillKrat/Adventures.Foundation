using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// Host aliases (2026-11-08): a tenant can be reached by more than its own domain, for example the hosting provider's temporary domain mirroring the real one. A host name decides
/// whose data a request sees, so a name can belong to one tenant only and is a plain host name (threat: one tenant claiming another's host).
/// </summary>
public sealed class TenantAliasTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var file in new[] { "seed.nq", "seed-004-identity.nq", "seed-005-identity-required.nq", "seed-020-tenant-aliases.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, file));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantSchemaIri), EntityConstants.Identity.TenantBaseIri, EntityConstants.Identity.TenantTypeIri, EntityConstants.Identity.DefaultGraph, s => new Tenant(s));
        services.AddScoped<IEntityBll<Tenant>>(p => new EntityBll<Tenant>(new EntityDal<Tenant>(p.GetRequiredService<IEntityRepository<Tenant>>())));
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantAliasEditor, TenantAliasEditor>();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ITenantAliasEditor Editor => _services.GetRequiredService<ITenantAliasEditor>();

    private ITenantResolver Resolver => _services.GetRequiredService<ITenantResolver>();

    private async Task<TenantHostInfo> TenantAsync(string name) => (await Editor.ListAsync()).Single(t => t.Name == name);

    [Fact]
    public async Task AnAliasReachesTheTenant_LikeItsDomain_AndTheTenantsOwnDomainStaysThePrimaryOne()
    {
        var main = await TenantAsync("DEV Main");
        Assert.Equal(["temp-1234.siteground.example"], await Editor.AddAsync(main.Id, "https://TEMP-1234.siteground.example:8443/path"));

        foreach (var host in new[] { "temp-1234.siteground.example", "api.temp-1234.siteground.example", "TEMP-1234.siteground.example:7052" })
        {
            var resolved = await Resolver.ResolveAsync(host);
            Assert.Equal(TenantMatch.Domain, resolved.Match);
            Assert.Equal(main.Id, resolved.Id);
            Assert.Equal(main.Domain, resolved.Domain);
        }

        Assert.Equal(TenantMatch.None, (await Resolver.ResolveAsync("evil-temp-1234.siteground.example")).Match);
    }

    [Fact]
    public async Task AnAliasCanBeRemovedAndTheHostNoLongerReachesTheTenant()
    {
        var main = await TenantAsync("DEV Main");
        await Editor.AddAsync(main.Id, "temp.example");
        await Editor.AddAsync(main.Id, "second.example");

        Assert.Equal(["second.example"], await Editor.RemoveAsync(main.Id, "TEMP.example"));

        Assert.Equal(TenantMatch.None, (await Resolver.ResolveAsync("temp.example")).Match);
        Assert.Equal(main.Id, (await Resolver.ResolveAsync("second.example")).Id);
        Assert.Equal(main.Domain, (await TenantAsync("DEV Main")).Domain);
    }

    [Fact]
    public async Task AHostThatAlreadyReachesAnotherTenantCannotBeClaimed_NotEvenBelowOrAboveIt()
    {
        var main = await TenantAsync("DEV Main");
        var edge = await TenantAsync("Adventures Edge");

        foreach (var host in new[] { edge.Domain, "www." + edge.Domain, "edge-extra." + edge.Domain })
        {
            var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => Editor.AddAsync(main.Id, host));
            Assert.Contains("only one tenant", refusal.Message);
        }

        await Editor.AddAsync(edge.Id, "mirror.example");
        await Assert.ThrowsAsync<BusinessRuleException>(() => Editor.AddAsync(main.Id, "mirror.example"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Editor.AddAsync(main.Id, "deep.mirror.example"));
        Assert.Equal(edge.Id, (await Resolver.ResolveAsync("mirror.example")).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a host")]
    [InlineData("bad_host.example")]
    [InlineData("-leading.example")]
    [InlineData("a..b.example")]
    public async Task OnlyAPlainHostNameIsAccepted(string host)
    {
        var main = await TenantAsync("DEV Main");

        await Assert.ThrowsAsync<BusinessRuleException>(() => Editor.AddAsync(main.Id, host));
        Assert.Empty((await TenantAsync("DEV Main")).Aliases);
    }

    [Fact]
    public async Task AnUnknownTenantIsNotFound_AndAddingTheSameAliasTwiceChangesNothing()
    {
        Assert.Null(await Editor.AddAsync(Guid.NewGuid().ToString(), "x.example"));
        Assert.Null(await Editor.RemoveAsync(Guid.NewGuid().ToString(), "x.example"));

        var main = await TenantAsync("DEV Main");
        await Editor.AddAsync(main.Id, "twice.example");
        Assert.Equal(["twice.example"], await Editor.AddAsync(main.Id, "TWICE.example"));
    }
}
