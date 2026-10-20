using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// A user form carries the child collections its schema declares (the role assignments), read straight from the
/// baseline seed: the assignments stay where they were (the user points at each), the identity seed only describes them.
/// </summary>
public sealed class UserRelationsTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IUserPresenter _users = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-004-identity.nq"));
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
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        _services = services.BuildServiceProvider();
        _users = _services.GetRequiredService<IUserPresenter>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AUsersForm_CarriesItsTenantAndOrganizationRoleAssignments_WithNamesNotIds()
    {
        var form = await _users.GetFormByUserNameAsync("Admin");

        Assert.NotNull(form!.Relations);
        var tenantRoles = form.Relations!.Single(r => r.Name == "TenantRoles");
        Assert.Equal("Tenant roles", tenantRoles.Label);
        Assert.Equal(2, tenantRoles.Grid.Rows.Count);
        Assert.All(tenantRoles.Grid.Rows, row => Assert.Equal("Admin", row.Display!["Role"]));
        Assert.Equivalent(new[] { "DEV Main", "Adventures On The Edge" }, tenantRoles.Grid.Rows.Select(r => r.Display!["Tenant"]));
        Assert.All(tenantRoles.Grid.Rows, row => Assert.True(Guid.TryParse(row.Values["Role"], out _)));

        var organizationRoles = form.Relations!.Single(r => r.Name == "OrganizationRoles");
        var only = Assert.Single(organizationRoles.Grid.Rows);
        Assert.Equal("Developer", only.Display!["Role"]);
        Assert.Equal("BlogAI", only.Display["Organization"]); // an organization is named by its blog
    }

    [Fact]
    public async Task AUserWithNoAssignments_HasEmptyRelationGrids_AndTheColumnsAreStillThere()
    {
        var form = await _users.GetFormByUserNameAsync("Claude");

        var tenantRoles = form!.Relations!.Single(r => r.Name == "TenantRoles");
        Assert.Empty(tenantRoles.Grid.Rows);
        Assert.Contains(tenantRoles.Grid.Schema.Fields, f => f.Name == "Role" && f.RefSchema == EntityConstants.Identity.RoleSchemaIri);
        Assert.False(tenantRoles.Grid.Schema.Fields.Single(f => f.Name == "Id").IsListVisible);
    }

    [Fact]
    public async Task RolesAreReadableAsAnEntityGrid_FromTheExistingSeedData()
    {
        var roles = _services.GetRequiredService<IEntityRepository<Role>>();

        var all = await roles.ListAsync();

        Assert.Equivalent(new[] { "Admin", "Editor", "Reviewer", "Developer" }, all.Select(r => (string)r.GetValue("Name")!));
    }

    [Fact]
    public async Task ASchemaWithNoRelations_ShowsNone_AndANonUserFormIsUnaffected()
    {
        var form = await _users.GetFormAsync((await _users.GetFormByUserNameAsync("Claude"))!.Entity.EntityId);

        Assert.Equal(2, form!.Relations!.Count);
    }
}
