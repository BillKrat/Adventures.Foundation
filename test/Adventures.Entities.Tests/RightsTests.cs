using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// The rights catalog and what a person may do: the union of the rights of their assigned roles, with the legacy TenantAdmin sign-in role as all.
/// baseline seed: the assignments stay where they were (the user points at each), the identity seed only describes them.
/// </summary>
public sealed class RightsTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IUserPresenter _users = null!;
    private IEntityRelationWriter _writer = null!;
    private EntitySchema _userSchema = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-004-identity.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-005-identity-required.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-018-role-rights.nq"));
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
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        services.AddScoped<IEntityBll<Role>>(p => new EntityBll<Role>(p.GetRequiredService<IEntityRepository<Role>>()));
        services.AddScoped<IRightsResolver, RightsResolver>();
        _services = services.BuildServiceProvider();
        _users = _services.GetRequiredService<IUserPresenter>();
        _writer = _services.GetRequiredService<IEntityRelationWriter>();
        _userSchema = _services.GetRequiredService<EntitySchemaOf<User>>().Schema;
    }

    public Task DisposeAsync() => Task.CompletedTask;
    private async Task<(string UserId, string AdminRoleId, string EditorRoleId, string TenantId)> IdsAsync()
    {
        var form = await _users.GetFormByUserNameAsync("Claude");
        var roles = await _services.GetRequiredService<IEntityRepository<Role>>().ListAsync();
        var tenants = await _services.GetRequiredService<IEntityRepository<Tenant>>().ListAsync();
        return (
            form!.Entity.EntityId,
            roles.Single(r => (string)r.GetValue("Name")! == "Admin").EntityId!.Value,
            roles.Single(r => (string)r.GetValue("Name")! == "Editor").EntityId!.Value,
            tenants.First().EntityId!.Value);
    }

    private static EntityDataModel Assignment(string tenantId, string roleId) =>
        new(string.Empty, new Dictionary<string, string?> { ["Tenant"] = tenantId, ["Role"] = roleId });


    private async Task<string> NewPersonAsync(string name) =>
        (await _users.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = name }))).Entity.EntityId;

    private async Task<string> RoleIdAsync(string name) =>
        (await _services.GetRequiredService<IEntityRepository<Role>>().ListAsync()).Single(r => (string)r.GetValue("Name")! == name).EntityId!.Value;

    [Fact]
    public void EveryRightHasAUniqueStableNameAndADescription_AndTheSeededRolesOnlyUseKnownRights()
    {
        Assert.Equal(Rights.Catalog.Count, Rights.Catalog.Select(r => r.Name).Distinct().Count());
        Assert.All(Rights.Catalog, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
        Assert.All(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-018-role-rights.nq")).Where(l => l.Contains("#rights> \"")),
            line => Rights.Parse(line.Split('"')[1]));
    }

    [Fact]
    public void AnUnknownRightIsRefused_AnyImpliesOwn_AndStarMeansEverything()
    {
        Assert.Throws<Adventures.Common.Exceptions.BusinessRuleException>(() => Rights.Parse("posts.read,posts.fly"));
        Assert.Contains(Rights.PostsEditOwn, Rights.Parse("posts.edit.any"));
        Assert.Equal(Rights.AllNames, Rights.Parse("*"));
        Assert.Empty(Rights.Parse(null));
    }

    [Fact]
    public async Task ThePersonWhoSignedInAsTheLegacyAdministratorHasEveryRight_EvenWithoutAssignments()
    {
        var rights = await _services.GetRequiredService<IRightsResolver>().ResolveAsync("nobody-here", ["TenantAdmin"]);

        Assert.Equal(Rights.AllNames, rights);
    }

    [Fact]
    public async Task ThePersonAssignedTheAdminRoleHasEveryRight_ThroughTheirAssignment()
    {
        var rights = await _services.GetRequiredService<IRightsResolver>().ResolveAsync("Admin", []);

        Assert.Equal(Rights.AllNames, rights);
    }

    [Fact]
    public async Task APersonGetsTheUnionOfTheirRolesRights_AndNothingElse()
    {
        var personId = await NewPersonAsync("test-mixed");
        var tenantId = (await _services.GetRequiredService<IEntityRepository<Tenant>>().ListAsync()).First().EntityId!.Value;
        await _writer.AddAsync(_userSchema, personId, "TenantRoles", Assignment(tenantId, await RoleIdAsync("Reviewer")));
        await _writer.AddAsync(_userSchema, personId, "TenantRoles", Assignment(tenantId, await RoleIdAsync("Developer")));

        var rights = await _services.GetRequiredService<IRightsResolver>().ResolveAsync("test-mixed", []);

        Assert.Contains(Rights.CollaborationReview, rights);
        Assert.Contains(Rights.CollaborationWrite, rights);
        Assert.Contains(Rights.DiagnosticsRead, rights);
        Assert.DoesNotContain(Rights.PostsPublish, rights);
        Assert.DoesNotContain(Rights.UsersManage, rights);
    }

    private async Task<string?> RightsTextOfAsync(string roleName) =>
        (await _services.GetRequiredService<IEntityRepository<Role>>().ListAsync()).Single(r => (string)r.GetValue("Name")! == roleName).GetValue("Rights") as string;

    [Fact]
    public async Task TheUpgradeLetsTheReviewerCommentAsWellAsApprove_AndLeavesTheEditorReadOnlyOnCollaboration()
    {
        var roles = _services.GetRequiredService<IEntityRepository<Role>>();
        Assert.DoesNotContain("collaboration.write", await RightsTextOfAsync("Reviewer"));

        Assert.Equal(1, await RoleRightsUpgrade.ApplyAsync(roles));

        var reviewer = Rights.Parse(await RightsTextOfAsync("Reviewer"));
        Assert.Contains(Rights.CollaborationReview, reviewer);
        Assert.Contains(Rights.CollaborationWrite, reviewer);
        Assert.DoesNotContain(Rights.CollaborationWrite, Rights.Parse(await RightsTextOfAsync("Editor")));
    }

    [Fact]
    public async Task TheUpgradeIsRepeatable_AndNeverOverwritesRightsAnAdministratorHasEdited()
    {
        var roles = _services.GetRequiredService<IEntityRepository<Role>>();
        Assert.Equal(1, await RoleRightsUpgrade.ApplyAsync(roles));
        Assert.Equal(0, await RoleRightsUpgrade.ApplyAsync(roles)); // already done

        var reviewer = (await roles.ListAsync()).Single(r => (string)r.GetValue("Name")! == "Reviewer");
        reviewer.Set("Rights", Guid.NewGuid().ToString(), "admin.access,posts.read");
        await roles.UpdateAsync(reviewer);
        Assert.Equal(0, await RoleRightsUpgrade.ApplyAsync(roles));
        Assert.Equal("admin.access,posts.read", await RightsTextOfAsync("Reviewer"));
    }

    [Fact]
    public async Task APersonWithNoRoleOrNoAccountHasNothing()
    {
        await NewPersonAsync("test-nobody");
        var resolver = _services.GetRequiredService<IRightsResolver>();

        Assert.Empty(await resolver.ResolveAsync("test-nobody", []));
        Assert.Empty(await resolver.ResolveAsync("not-a-person", []));
        Assert.Empty(await resolver.ResolveAsync(string.Empty, ["User"]));
    }
}
