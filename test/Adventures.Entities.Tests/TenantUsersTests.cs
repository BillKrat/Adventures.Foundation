using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// T3b: the people an administrator may see and change are the members of the request's tenant. Threats: cross-tenant read and write of people, deleting a person
/// who also belongs to another tenant, and a membership turning into rights. The seeded people belong to DEV Main (a2b7af6d); a second tenant starts with nobody.
/// </summary>
public sealed class TenantUsersTests : IAsyncLifetime
{
    private const string DevMain = "a2b7af6d-4c2b-4732-b837-1e6d49b2c05d";
    private const string OtherTenant = "31fc5222-4904-4ffd-b621-5d814829aaeb"; // Adventures Edge: the seed gives it no assignments
    private const string DevMainOrganisation = "d8043cfc-5917-4537-a24d-1a79f0a76c40";

    private sealed class Fixed(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
    }

    private sealed class NoTenant : ITenantContext
    {
        public string TenantId => throw new TenantRequiredException();
    }

    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var file in new[] { "seed.nq", "seed-004-identity.nq", "seed-005-identity-required.nq", "seed-018-role-rights.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, file));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        const string graph = EntityConstants.Identity.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Schema.UserIri), EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, EntityConstants.User.DefaultGraph, s => new User(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.RoleSchemaIri), EntityConstants.Identity.RoleBaseIri, EntityConstants.Identity.RoleTypeIri, graph, s => new Role(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantSchemaIri), EntityConstants.Identity.TenantBaseIri, EntityConstants.Identity.TenantTypeIri, graph, s => new Tenant(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.BlogSchemaIri), EntityConstants.Identity.BlogBaseIri, EntityConstants.Identity.BlogTypeIri, graph, s => new Blog(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.OrganizationSchemaIri), EntityConstants.Identity.OrganizationBaseIri, EntityConstants.Identity.OrganizationTypeIri, graph, s => new Organization(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantRoleSchemaIri), EntityConstants.Identity.TenantRoleBaseIri, EntityConstants.Identity.TenantRoleTypeIri, graph, s => new TenantRoleAssignment(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.OrganizationRoleSchemaIri), EntityConstants.Identity.OrganizationRoleBaseIri, EntityConstants.Identity.OrganizationRoleTypeIri, graph, s => new OrganizationRoleAssignment(s));
        services.AddNQuadRelations();
        services.AddNQuadRelationWriter();
        services.AddNQuadUserMembership();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IEntityBll<Role>>(p => new EntityBll<Role>(new EntityDal<Role>(p.GetRequiredService<IEntityRepository<Role>>())));
        services.AddScoped<IEntityBll<Organization>>(p => new EntityBll<Organization>(new EntityDal<Organization>(p.GetRequiredService<IEntityRepository<Organization>>())));
        services.AddScoped<IRightsResolver, RightsResolver>();
        services.AddNQuadPlatformAdministrators();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TenantScopedUserBll For(string tenantId, IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<IUserBll>(), scope.ServiceProvider.GetRequiredService<IUserMembership>(), new Fixed(tenantId));

    private static User NewUser(IServiceScope scope, string name)
    {
        var user = new User(scope.ServiceProvider.GetRequiredService<EntitySchemaOf<User>>().Schema);
        var id = Guid.NewGuid().ToString();
        user.Set(DynamicEntity.EntityIdPropertyName, id, id);
        user.Set("UserName", Guid.NewGuid().ToString(), name);
        return user;
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(TenantScopedUserBll bll) =>
        (await bll.ListAsync()).Select(u => (string)u.GetValue("UserName")!).Order().ToList();

    [Fact]
    public async Task APersonAddedInOneTenantIsSeenOnlyThere_AndASecondTenantStartsWithNobody()
    {
        using var scope = _services.CreateScope();
        var created = await For(OtherTenant, scope).CreateAsync(NewUser(scope, "test-new-in-other"));

        Assert.Equal(["test-new-in-other"], await NamesAsync(For(OtherTenant, scope)));
        Assert.DoesNotContain("test-new-in-other", await NamesAsync(For(DevMain, scope)));
        Assert.Contains("Admin", await NamesAsync(For(DevMain, scope)));
        Assert.DoesNotContain("Admin", await NamesAsync(For(OtherTenant, scope)));
        Assert.NotNull(await For(OtherTenant, scope).GetAsync(created.EntityId!.Value));
    }

    [Fact]
    public async Task AnotherTenantsPersonCannotBeOpenedChangedOrDeleted()
    {
        using var scope = _services.CreateScope();
        var admin = (await For(DevMain, scope).ListAsync()).First(u => (string)u.GetValue("UserName")! == "Admin");
        var id = admin.EntityId!.Value;
        var outsider = For(OtherTenant, scope);

        Assert.Null(await outsider.GetAsync(id));
        admin.Set("DisplayName", Guid.NewGuid().ToString(), "hijacked");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => outsider.UpdateAsync(admin));
        Assert.False(await outsider.DeleteAsync(id, "someone-else"));
        Assert.NotNull(await For(DevMain, scope).GetAsync(id));
    }

    [Fact]
    public async Task APersonWhoAlsoBelongsToAnotherTenantCannotBeDeletedFromHere_ButOnePersonOfOneTenantCan()
    {
        using var scope = _services.CreateScope();
        var shared = await For(DevMain, scope).CreateAsync(NewUser(scope, "test-shared"));
        await scope.ServiceProvider.GetRequiredService<IUserMembership>().AddMemberAsync(shared.EntityId!.Value, OtherTenant);
        var solo = await For(DevMain, scope).CreateAsync(NewUser(scope, "test-solo"));

        await Assert.ThrowsAsync<BusinessRuleException>(() => For(DevMain, scope).DeleteAsync(shared.EntityId!.Value, "someone-else"));
        Assert.True(await For(DevMain, scope).DeleteAsync(solo.EntityId!.Value, "someone-else"));
        Assert.Null(await For(DevMain, scope).GetAsync(solo.EntityId!.Value));
    }

    [Fact]
    public async Task AMembershipGivesNoRights_AndAddingItTwiceChangesNothing()
    {
        using var scope = _services.CreateScope();
        var person = await For(DevMain, scope).CreateAsync(NewUser(scope, "test-member-only"));
        var membership = scope.ServiceProvider.GetRequiredService<IUserMembership>();
        await membership.AddMemberAsync(person.EntityId!.Value, DevMain);

        Assert.Equal([DevMain], await membership.TenantIdsOfAsync(person.EntityId!.Value));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IRightsResolver>().ResolveAsync("test-member-only", [], DevMain));
    }

    [Fact]
    public async Task AnOrganisationBelongsToItsOwnTenantOnly()
    {
        using var scope = _services.CreateScope();
        var membership = scope.ServiceProvider.GetRequiredService<IUserMembership>();

        Assert.True(await membership.OrganisationInTenantAsync(DevMainOrganisation, DevMain));
        Assert.False(await membership.OrganisationInTenantAsync(DevMainOrganisation, OtherTenant));
        Assert.False(await membership.OrganisationInTenantAsync("no-such-organisation", DevMain));
    }

    [Fact]
    public async Task APlatformAdministratorWhoAsksSeesEveryoneAndCanOpenAnyPerson_ButEveryoneElseStillSeesOnlyTheirTenant()
    {
        using var scope = _services.CreateScope();
        var everyone = new UserVisibility { SeeEveryone = true };
        var platform = new TenantScopedUserBll(
            scope.ServiceProvider.GetRequiredService<IUserBll>(), scope.ServiceProvider.GetRequiredService<IUserMembership>(), new Fixed(OtherTenant), everyone);
        var ordinary = For(OtherTenant, scope);
        var admin = (await For(DevMain, scope).ListAsync()).First(u => (string)u.GetValue("UserName")! == "Admin");

        Assert.Contains("Admin", await NamesAsync(platform));
        Assert.NotNull(await platform.GetAsync(admin.EntityId!.Value));
        Assert.DoesNotContain("Admin", await NamesAsync(ordinary));
        Assert.Null(await ordinary.GetAsync(admin.EntityId!.Value));
    }

    [Fact]
    public async Task WithNoTenantNothingIsListedOrCreated()
    {
        using var scope = _services.CreateScope();
        var bll = new TenantScopedUserBll(scope.ServiceProvider.GetRequiredService<IUserBll>(), scope.ServiceProvider.GetRequiredService<IUserMembership>(), new NoTenant());

        await Assert.ThrowsAsync<TenantRequiredException>(() => bll.ListAsync());
        await Assert.ThrowsAsync<TenantRequiredException>(() => bll.CreateAsync(NewUser(scope, "test-nowhere")));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IUserBll>().FindByUserNameAsync("test-nowhere"));
    }
}
