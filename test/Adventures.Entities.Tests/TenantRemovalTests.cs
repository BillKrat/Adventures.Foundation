using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// Deleting a tenant (human request, 2026-11-21): a soft delete of the Tenant record, so its domain and host names stop reaching anything. It is the platform
/// administrator's act, needs the tenant's domain typed as confirmation, and refuses the tenant the person is working in, the tenant that owns the host the request came on
/// and the last tenant, so nobody can delete the site out from under themselves. The tenant's content is not touched (it stays in its own graph, unreachable).
/// </summary>
public sealed class TenantRemovalTests : IAsyncLifetime
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
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, IdentityConstants.TenantSchemaIri), IdentityConstants.TenantBaseIri, IdentityConstants.TenantTypeIri, IdentityConstants.DefaultGraph, s => new Tenant(s));
        services.AddScoped<IEntityBll<Tenant>>(p => new EntityBll<Tenant>(new EntityDal<Tenant>(p.GetRequiredService<IEntityRepository<Tenant>>())));
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantAliasEditor, TenantAliasEditor>();
        services.AddScoped<ITenantRemover, TenantRemover>();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ITenantAliasEditor Editor => _services.GetRequiredService<ITenantAliasEditor>();

    private ITenantRemover Remover => _services.GetRequiredService<ITenantRemover>();

    private async Task<TenantHostInfo> TenantAsync(string name) => (await Editor.ListAsync()).Single(t => t.Name == name);

    private static readonly IReadOnlyCollection<string> NothingProtected = [];

    [Fact]
    public async Task ADeletedTenant_IsGone_AndItsDomainAndAliasesReachNothing()
    {
        var edge = await TenantAsync("Adventures Edge");
        await Editor.AddAsync(edge.Id, "edge-temp.example.test");

        var removed = await Remover.RemoveAsync(edge.Id, edge.Domain, NothingProtected);

        Assert.NotNull(removed);
        Assert.Equal("Adventures Edge", removed!.Name);
        Assert.DoesNotContain(await Editor.ListAsync(), t => t.Id == edge.Id);
        var resolver = _services.GetRequiredService<ITenantResolver>();
        Assert.Equal(TenantMatch.None, (await resolver.ResolveAsync(edge.Domain)).Match);
        Assert.Equal(TenantMatch.None, (await resolver.ResolveAsync("edge-temp.example.test")).Match);
        Assert.Contains(await Editor.ListAsync(), t => t.Name == "DEV Main");
    }

    [Fact]
    public async Task TheDomainMustBeTypedToConfirm_AWrongOrEmptyOneDeletesNothing()
    {
        var edge = await TenantAsync("Adventures Edge");

        await Assert.ThrowsAsync<BusinessRuleException>(() => Remover.RemoveAsync(edge.Id, "something-else.test", NothingProtected));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Remover.RemoveAsync(edge.Id, "", NothingProtected));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Remover.RemoveAsync(edge.Id, null, NothingProtected));

        Assert.Contains(await Editor.ListAsync(), t => t.Id == edge.Id);
    }

    [Fact]
    public async Task TheConfirmation_IgnoresCaseSchemeAndPort_LikeEveryOtherHostName()
    {
        var edge = await TenantAsync("Adventures Edge");

        Assert.NotNull(await Remover.RemoveAsync(edge.Id, $"HTTPS://{edge.Domain.ToUpperInvariant()}:443/", NothingProtected));
    }

    [Fact]
    public async Task ATenantThePersonIsWorkingInOrReachingThisHostThrough_IsRefused_WithAWayOut()
    {
        var edge = await TenantAsync("Adventures Edge");

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => Remover.RemoveAsync(edge.Id, edge.Domain, [edge.Id]));

        Assert.Contains("another tenant", refusal.Message);
        Assert.Contains(await Editor.ListAsync(), t => t.Id == edge.Id);
    }

    [Fact]
    public async Task TheLastTenantIsNeverDeleted()
    {
        var main = await TenantAsync("DEV Main");
        foreach (var other in (await Editor.ListAsync()).Where(t => t.Id != main.Id))
        {
            await Remover.RemoveAsync(other.Id, other.Domain, NothingProtected);
        }

        Assert.Single(await Editor.ListAsync());

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => Remover.RemoveAsync(main.Id, main.Domain, NothingProtected));

        Assert.Contains("last tenant", refusal.Message);
        Assert.Contains(await Editor.ListAsync(), t => t.Id == main.Id);
    }

    [Fact]
    public async Task AnUnknownTenant_IsNull_AndDeletingTwiceIsHarmless()
    {
        var edge = await TenantAsync("Adventures Edge");

        Assert.Null(await Remover.RemoveAsync("no-such-id", "x.test", NothingProtected));
        await Remover.RemoveAsync(edge.Id, edge.Domain, NothingProtected);
        Assert.Null(await Remover.RemoveAsync(edge.Id, edge.Domain, NothingProtected));
    }
}
