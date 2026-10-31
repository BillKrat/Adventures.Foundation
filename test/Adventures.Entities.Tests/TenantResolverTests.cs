using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// One deployed app serves many domains: the host name of the request picks the tenant (a Tenant's Domain field), so a code update is
/// made once. A host matches a tenant when it is the domain itself or a subdomain of it (api., www.), never when it merely ends with the same
/// letters. An unknown host has no tenant, unless the host configured a fallback domain for development (localhost).
/// </summary>
public sealed class TenantResolverTests : IAsyncLifetime
{
    private ITenantResolver _resolver = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-004-identity.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-005-identity-required.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantSchemaIri), EntityConstants.Identity.TenantBaseIri, EntityConstants.Identity.TenantTypeIri, EntityConstants.Identity.DefaultGraph, s => new Tenant(s));
        services.AddScoped<IEntityBll<Tenant>>(p => new EntityBll<Tenant>(new EntityDal<Tenant>(p.GetRequiredService<IEntityRepository<Tenant>>())));
        services.AddScoped<ITenantResolver, TenantResolver>();
        _resolver = services.BuildServiceProvider().GetRequiredService<ITenantResolver>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("global-webnet.com", "DEV Main")]
    [InlineData("GLOBAL-WEBNET.COM", "DEV Main")]
    [InlineData("global-webnet.com:7052", "DEV Main")]
    [InlineData("api.global-webnet.com", "DEV Main")]
    [InlineData("www.adventuresontheedge.net", "Adventures On The Edge")]
    [InlineData("adventuresEdge.net", "Adventures Edge")]
    public async Task AHostMatchesTheTenantWhoseDomainItIsOrSitsUnder(string host, string expectedName)
    {
        var tenant = await _resolver.ResolveAsync(host);

        Assert.Equal(TenantMatch.Domain, tenant.Match);
        Assert.Equal(expectedName, tenant.Name);
        Assert.False(string.IsNullOrEmpty(tenant.Id));
    }

    [Theory]
    [InlineData("evilglobal-webnet.com")]
    [InlineData("global-webnet.com.evil.example")]
    [InlineData("localhost:4200")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AHostThatIsNotUnderAnyTenantDomainHasNoTenant_NotEvenOneThatEndsWithTheSameLetters(string? host)
    {
        var tenant = await _resolver.ResolveAsync(host);

        Assert.Equal(TenantMatch.None, tenant.Match);
        Assert.Null(tenant.Name);
        Assert.Null(tenant.Id);
    }

    [Fact]
    public async Task ADevelopmentFallbackDomainNamesTheTenantForAHostThatMatchesNone_AndSaysSo()
    {
        var tenant = await _resolver.ResolveAsync("localhost:4200", fallbackDomain: "global-webnet.com");

        Assert.Equal(TenantMatch.Fallback, tenant.Match);
        Assert.Equal("DEV Main", tenant.Name);
        Assert.Equal("localhost:4200", tenant.Host);
    }

    [Fact]
    public async Task ARealMatchBeatsTheFallback()
    {
        var tenant = await _resolver.ResolveAsync("adventuresedge.net", fallbackDomain: "global-webnet.com");

        Assert.Equal(TenantMatch.Domain, tenant.Match);
        Assert.Equal("Adventures Edge", tenant.Name);
    }

    [Fact]
    public async Task AFallbackThatNamesNoTenantChangesNothing()
    {
        var tenant = await _resolver.ResolveAsync("localhost", fallbackDomain: "nobody.example");

        Assert.Equal(TenantMatch.None, tenant.Match);
    }
}
