using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// T4b. A platform administrator creates a tenant on a domain from one place (name and domain, nothing else to prepare), and an operator can let the temporary address
/// of a fresh install reach the tenant it names without anyone signing in first. Both decide whose data a host name shows, so a host name belongs to one tenant only,
/// must be a real name (never an address or localhost) and an automatic alias is capped (threats: claiming another tenant's host, an endless stream of aliases).
/// </summary>
public sealed class TenantCreationAndFirstRunTests : IAsyncLifetime
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
        services.AddScoped<ITenantCreator, TenantCreator>();
        services.AddScoped<ITenantFirstRun, TenantFirstRun>();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ITenantAliasEditor Editor => _services.GetRequiredService<ITenantAliasEditor>();

    private ITenantCreator Creator => _services.GetRequiredService<ITenantCreator>();

    private ITenantFirstRun FirstRun => _services.GetRequiredService<ITenantFirstRun>();

    private ITenantResolver Resolver => _services.GetRequiredService<ITenantResolver>();

    private async Task<TenantHostInfo> MainAsync() => (await Editor.ListAsync()).Single(t => t.Name == "DEV Main");

    // ---- creating a tenant

    [Fact]
    public async Task ANewTenant_IsReachedByItsDomain_AndByNamesUnderIt_AndAppearsInTheList()
    {
        var created = await Creator.CreateAsync("  Photos by Di ", "https://Photos.Example.org/path");

        Assert.Equal(("Photos by Di", "photos.example.org"), (created.Name, created.Domain));
        Assert.Empty(created.Aliases);
        foreach (var host in new[] { "photos.example.org", "www.photos.example.org", "PHOTOS.example.org:8443" })
        {
            var resolved = await Resolver.ResolveAsync(host);
            Assert.Equal(created.Id, resolved.Id);
        }

        Assert.Contains(await Editor.ListAsync(), t => t.Id == created.Id);
    }

    [Fact]
    public async Task TwoNewTenants_CanLiveSideBySide()
    {
        var a = await Creator.CreateAsync("A", "a.example.org");
        var b = await Creator.CreateAsync("B", "b.example.org");

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(a.Id, (await Resolver.ResolveAsync("a.example.org")).Id);
        Assert.Equal(b.Id, (await Resolver.ResolveAsync("b.example.org")).Id);
    }

    [Fact]
    public async Task ADomainAnotherTenantAlreadyReaches_IsRefused_SoNobodyCanClaimAHostThatIsNotTheirs()
    {
        var main = await MainAsync();
        await Editor.AddAsync(main.Id, "temp.example.net");

        foreach (var domain in new[] { main.Domain, "www." + main.Domain, "temp.example.net", "sub.temp.example.net" })
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => Creator.CreateAsync("Thief", domain));
        }

        await Creator.CreateAsync("First", "mine.example.org");
        await Assert.ThrowsAsync<BusinessRuleException>(() => Creator.CreateAsync("Second", "mine.example.org"));
        Assert.Equal(1, (await Editor.ListAsync()).Count(t => t.Name is "First" or "Second"));
    }

    [Theory]
    [InlineData("", "ok.example.org")]
    [InlineData("   ", "ok.example.org")]
    [InlineData("Fine", "")]
    [InlineData("Fine", "not a host")]
    [InlineData("Fine", "localhost")]
    [InlineData("Fine", "127.0.0.1")]
    [InlineData("Fine", "10.0.0.5")]
    [InlineData("Fine", "nodots")]
    [InlineData("Fine", "bad_host.example.org")]
    public async Task ANameAndARealDomainAreRequired(string name, string domain)
    {
        var before = (await Editor.ListAsync()).Count;

        await Assert.ThrowsAsync<BusinessRuleException>(() => Creator.CreateAsync(name, domain));

        Assert.Equal(before, (await Editor.ListAsync()).Count);
    }

    [Fact]
    public async Task ATooLongName_IsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Creator.CreateAsync(new string('n', TenantCreator.MaxNameLength + 1), "long.example.org"));
        await Creator.CreateAsync(new string('n', TenantCreator.MaxNameLength), "fits.example.org");
    }

    // ---- first run

    [Fact]
    public async Task AnUnknownHost_BecomesAnAliasOfTheNamedTenant_AndThenReachesIt()
    {
        var main = await MainAsync();

        var done = await FirstRun.TryAliasAsync("billkrat-001-site1.atempurl.com:80", main.Domain);

        Assert.True(done);
        Assert.Contains("billkrat-001-site1.atempurl.com", (await Editor.ListAsync()).Single(t => t.Id == main.Id).Aliases);
        Assert.Equal(main.Id, (await Resolver.ResolveAsync("billkrat-001-site1.atempurl.com")).Id);
    }

    [Fact]
    public async Task NothingHappens_WhenTheNamedTenantDoesNotExist_OrTheHostAlreadyReachesATenant()
    {
        var main = await MainAsync();
        var before = (await Editor.ListAsync()).Select(t => (t.Id, string.Join(',', t.Aliases))).ToList();

        Assert.False(await FirstRun.TryAliasAsync("new.example.org", "no-such-tenant.example"));
        Assert.False(await FirstRun.TryAliasAsync("www." + main.Domain, main.Domain));   // already reaches it
        Assert.False(await FirstRun.TryAliasAsync("anything.example.org", null));
        Assert.False(await FirstRun.TryAliasAsync("anything.example.org", " "));

        Assert.Equal(before, (await Editor.ListAsync()).Select(t => (t.Id, string.Join(',', t.Aliases))).ToList());
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("[::1]")]
    [InlineData("not a host")]
    [InlineData("")]
    public async Task OnlyARealHostName_IsEverAliased_NeverAnAddressLocalhostOrJunk(string host)
    {
        var main = await MainAsync();

        Assert.False(await FirstRun.TryAliasAsync(host, main.Domain));

        Assert.Empty((await Editor.ListAsync()).Single(t => t.Id == main.Id).Aliases);
    }

    [Fact]
    public async Task AnAutomaticAlias_IsCapped_SoItCannotGrowWithoutEnd()
    {
        var main = await MainAsync();
        for (var n = 0; n < TenantFirstRun.MaxAutomaticAliases; n++)
        {
            Assert.True(await FirstRun.TryAliasAsync($"temp{n}.example.org", main.Domain));
        }

        Assert.False(await FirstRun.TryAliasAsync("one-too-many.example.org", main.Domain));
        Assert.Equal(TenantFirstRun.MaxAutomaticAliases, (await Editor.ListAsync()).Single(t => t.Id == main.Id).Aliases.Count);
    }
}
