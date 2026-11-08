using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>Which tenants a person may act in (the tenant switcher): the ones they belong to, or every one for a platform administrator; nobody else.</summary>
public sealed class ActingTenantsTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var file in new[] { "seed.nq", "seed-004-identity.nq", "seed-005-identity-required.nq", "seed-018-role-rights.nq", "seed-020-tenant-aliases.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, file));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        const string graph = EntityConstants.Identity.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Schema.UserIri), EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, EntityConstants.User.DefaultGraph, s => new User(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantSchemaIri), EntityConstants.Identity.TenantBaseIri, EntityConstants.Identity.TenantTypeIri, graph, s => new Tenant(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.OrganizationSchemaIri), EntityConstants.Identity.OrganizationBaseIri, EntityConstants.Identity.OrganizationTypeIri, graph, s => new Organization(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantRoleSchemaIri), EntityConstants.Identity.TenantRoleBaseIri, EntityConstants.Identity.TenantRoleTypeIri, graph, s => new TenantRoleAssignment(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.OrganizationRoleSchemaIri), EntityConstants.Identity.OrganizationRoleBaseIri, EntityConstants.Identity.OrganizationRoleTypeIri, graph, s => new OrganizationRoleAssignment(s));
        services.AddNQuadRelations();
        services.AddNQuadUserMembership();
        services.AddNQuadPlatformAdministrators();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IEntityBll<Tenant>>(p => new EntityBll<Tenant>(new EntityDal<Tenant>(p.GetRequiredService<IEntityRepository<Tenant>>())));
        services.AddScoped<ITenantAliasEditor, TenantAliasEditor>();
        services.AddScoped<IActingTenants, ActingTenants>();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IActingTenants Acting => _services.GetRequiredService<IActingTenants>();

    private async Task<IReadOnlyList<string>> NamesAsync(string userName) => (await Acting.ForAsync(userName)).Select(t => t.Name).Order().ToList();

    [Fact]
    public async Task APersonMayActInTheTenantsTheyBelongTo_AndInNoOther()
    {
        var claude = (await _services.GetRequiredService<IUserBll>().FindByUserNameAsync("Claude"))!.EntityId!.Value;
        var first = (await _services.GetRequiredService<ITenantAliasEditor>().ListAsync()).First();
        await _services.GetRequiredService<IUserMembership>().AddMemberAsync(claude, first.Id);
        var names = await NamesAsync("Claude");

        Assert.NotEmpty(names);
        var all = await _services.GetRequiredService<ITenantAliasEditor>().ListAsync();
        Assert.True(names.Count < all.Count, "a person who belongs to some tenants must not be offered all of them");
        foreach (var tenant in await Acting.ForAsync("Claude"))
        {
            Assert.NotNull(await Acting.AllowedAsync("Claude", tenant.Id));
        }

        var outside = all.First(t => !names.Contains(t.Name));
        Assert.Null(await Acting.AllowedAsync("Claude", outside.Id));
    }

    [Fact]
    public async Task APlatformAdministratorMayActInEveryTenant()
    {
        var claude = (await _services.GetRequiredService<IUserBll>().FindByUserNameAsync("Claude"))!.EntityId!.Value;
        await _services.GetRequiredService<IPlatformAdministrators>().SetAsync(claude, true, "test");

        var all = (await _services.GetRequiredService<ITenantAliasEditor>().ListAsync()).Select(t => t.Name).Order().ToList();
        Assert.Equal(all, await NamesAsync("Claude"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nobody-here")]
    public async Task AnUnknownPersonMayActNowhere(string userName)
    {
        Assert.Empty(await Acting.ForAsync(userName));
        Assert.Null(await Acting.AllowedAsync(userName, "anything"));
    }

    [Fact]
    public async Task ABlankOrUnknownTenantIsNotAllowedForAnyone()
    {
        Assert.Null(await Acting.AllowedAsync("Admin", string.Empty));
        Assert.Null(await Acting.AllowedAsync("Admin", Guid.NewGuid().ToString()));
    }
}
