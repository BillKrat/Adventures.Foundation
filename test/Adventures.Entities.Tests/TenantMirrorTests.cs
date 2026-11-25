using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// The mirror pointer (decided 2026-11-25): a tenant can use the configuration of another tenant. The pointer lives on the requesting tenant, whose own record and data are never
/// touched, only ignored while the pointer is set; clearing it resumes them. One level only. Threats addressed: a pointer chain or loop, a tenant mirroring itself, a pointer to
/// something that is not a tenant, and a tenant that stops being reachable because of a stale pointer.
/// </summary>
public sealed class TenantMirrorTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var file in new[] { "seed.nq", "seed-004-identity.nq", "seed-005-identity-required.nq", "seed-020-tenant-aliases.nq", "seed-028-tenant-mirrors.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, file));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, IdentityConstants.TenantSchemaIri), IdentityConstants.TenantBaseIri, IdentityConstants.TenantTypeIri, IdentityConstants.DefaultGraph, s => new Tenant(s));
        services.AddScoped<IEntityBll<Tenant>>(p => new EntityBll<Tenant>(new EntityDal<Tenant>(p.GetRequiredService<IEntityRepository<Tenant>>())));
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantAliasEditor, TenantAliasEditor>();
        services.AddScoped<ITenantMirrors, TenantMirrors>();
        _services = services.BuildServiceProvider();
        var creator = ActivatorUtilities.CreateInstance<TenantCreator>(_services);
        await creator.CreateAsync("Scratch", "scratch.example");
        await creator.CreateAsync("Third", "third.example");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ITenantMirrors Mirrors => _services.GetRequiredService<ITenantMirrors>();

    private ITenantResolver Resolver => _services.GetRequiredService<ITenantResolver>();

    private IEntityBll<Tenant> Tenants => _services.GetRequiredService<IEntityBll<Tenant>>();

    private async Task<TenantHostInfo> TenantAsync(string name) => (await _services.GetRequiredService<ITenantAliasEditor>().ListAsync()).Single(t => t.Name == name);

    [Fact]
    public async Task APointer_ServesTheTargetsConfiguration_OnTheHostsOwnAddresses_AndSaysWhoIsMirroring()
    {
        var main = await TenantAsync("DEV Main");
        var scratch = await TenantAsync("Scratch");

        Assert.True(await Mirrors.SetAsync(scratch.Id, main.Id));

        var resolved = await Resolver.ResolveAsync("scratch.example");
        Assert.Equal(main.Id, resolved.Id);
        Assert.Equal(main.Domain, resolved.Domain);
        Assert.Equal(TenantMatch.Mirror, resolved.Match);
        Assert.Equal(scratch.Id, resolved.OwnId);
        Assert.Equal("scratch.example", resolved.OwnDomain);
        Assert.Equal(main.Id, (await Resolver.ResolveAsync("api.scratch.example")).Id);   // a name under the tenant's domain is mirrored too
    }

    [Fact]
    public async Task TheTargetsOwnAddresses_AreUnchanged()
    {
        var main = await TenantAsync("DEV Main");
        await Mirrors.SetAsync((await TenantAsync("Scratch")).Id, main.Id);

        var resolved = await Resolver.ResolveAsync(main.Domain);

        Assert.Equal(main.Id, resolved.Id);
        Assert.Equal(TenantMatch.Domain, resolved.Match);
        Assert.Null(resolved.OwnId);
    }

    [Fact]
    public async Task ClearingThePointer_ResumesTheTenantsOwnConfiguration_AndNothingWasChanged()
    {
        var main = await TenantAsync("DEV Main");
        var before = await TenantAsync("Scratch");
        await Mirrors.SetAsync(before.Id, main.Id);

        Assert.True(await Mirrors.SetAsync(before.Id, null));

        var resolved = await Resolver.ResolveAsync("scratch.example");
        Assert.Equal(before.Id, resolved.Id);
        Assert.Equal(TenantMatch.Domain, resolved.Match);
        var after = await TenantAsync("Scratch");
        Assert.Equal((before.Name, before.Domain), (after.Name, after.Domain));
        Assert.Equal(before.Aliases, after.Aliases);
        Assert.True(string.IsNullOrEmpty(after.Mirrors));
    }

    [Fact]
    public async Task TheListShowsEachTenantsPointer()
    {
        var main = await TenantAsync("DEV Main");
        var scratch = await TenantAsync("Scratch");
        await Mirrors.SetAsync(scratch.Id, main.Id);

        Assert.Equal(main.Id, (await TenantAsync("Scratch")).Mirrors);
        Assert.True(string.IsNullOrEmpty((await TenantAsync("DEV Main")).Mirrors));
    }

    [Fact]
    public async Task ATenantCannotMirrorItself_OrSomethingThatIsNotATenant()
    {
        var scratch = await TenantAsync("Scratch");

        await Assert.ThrowsAsync<BusinessRuleException>(() => Mirrors.SetAsync(scratch.Id, scratch.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Mirrors.SetAsync(scratch.Id, "not-a-tenant-id"));
        Assert.True(string.IsNullOrEmpty((await TenantAsync("Scratch")).Mirrors));
    }

    [Fact]
    public async Task OnlyOneLevel_ATenantThatMirrorsCannotBeATarget_AndATargetCannotStartToMirror()
    {
        var main = await TenantAsync("DEV Main");
        var scratch = await TenantAsync("Scratch");
        var third = await TenantAsync("Third");
        await Mirrors.SetAsync(scratch.Id, main.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Mirrors.SetAsync(third.Id, scratch.Id));   // scratch mirrors, so it is no target
        await Assert.ThrowsAsync<BusinessRuleException>(() => Mirrors.SetAsync(main.Id, third.Id));      // main is a target of scratch, so it cannot start to mirror
    }

    [Fact]
    public async Task AnUnknownTenant_IsNotFound_AndNothingIsSet()
    {
        Assert.False(await Mirrors.SetAsync("no-such-tenant", (await TenantAsync("DEV Main")).Id));
    }

    [Fact]
    public async Task APointerToADeletedTenant_IsIgnored_SoTheHostServesItsOwnTenant()
    {
        var main = await TenantAsync("DEV Main");
        var scratch = await TenantAsync("Scratch");
        var third = await TenantAsync("Third");
        await Mirrors.SetAsync(scratch.Id, third.Id);

        await Tenants.DeleteAsync(third.Id);

        var resolved = await Resolver.ResolveAsync("scratch.example");
        Assert.Equal(scratch.Id, resolved.Id);
        Assert.Equal(TenantMatch.Domain, resolved.Match);
        Assert.NotEqual(main.Id, resolved.Id);
    }

    [Fact]
    public async Task AStalePointerChain_IsNotFollowed_TheHostServesItsOwnTenant()
    {
        var main = await TenantAsync("DEV Main");
        var scratch = await TenantAsync("Scratch");
        var third = await TenantAsync("Third");
        await Mirrors.SetAsync(scratch.Id, third.Id);
        // bypass the rules, as old data or a direct edit could: the target now mirrors as well
        var target = (await Tenants.GetAsync(third.Id))!;
        target.Set("Mirrors", Guid.NewGuid().ToString(), main.Id);
        await Tenants.UpdateAsync(target);

        var resolved = await Resolver.ResolveAsync("scratch.example");

        Assert.Equal(scratch.Id, resolved.Id);
        Assert.Equal(TenantMatch.Domain, resolved.Match);
    }
}
