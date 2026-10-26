using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// Editing what a role allows (S3b). Threats addressed: a role edit that leaves nobody able to manage users and roles (lock-out of the whole
/// tenant), an administrator taking away their own ability to administer, an unknown right slipping into storage, and an edit nobody can trace.
/// </summary>
public sealed class RoleRightsEditorTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IUserPresenter _users = null!;
    private IEntityRelationWriter _writer = null!;
    private IRoleRightsEditor _editor = null!;
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
        services.AddScoped<IRoleRightsEditor, RoleRightsEditor>();
        _services = services.BuildServiceProvider();
        _users = _services.GetRequiredService<IUserPresenter>();
        _writer = _services.GetRequiredService<IEntityRelationWriter>();
        _editor = _services.GetRequiredService<IRoleRightsEditor>();
        _userSchema = _services.GetRequiredService<EntitySchemaOf<User>>().Schema;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> RoleIdAsync(string name) =>
        (await _services.GetRequiredService<IEntityRepository<Role>>().ListAsync()).Single(r => (string)r.GetValue("Name")! == name).EntityId!.Value;

    private async Task<string> RightsOfAsync(string roleId) =>
        (string)(await _services.GetRequiredService<IEntityRepository<Role>>().GetAsync(roleId))!.GetValue("Rights")!;

    /// <summary>A new person who holds only the Editor role.</summary>
    private async Task<string> EditorPersonAsync(string name)
    {
        var id = (await _users.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = name }))).Entity.EntityId;
        var tenant = (await _services.GetRequiredService<IEntityRepository<Tenant>>().ListAsync()).First().EntityId!.Value;
        await _writer.AddAsync(_userSchema, id, "TenantRoles", new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Tenant"] = tenant, ["Role"] = await RoleIdAsync("Editor") }));
        return id;
    }

    [Fact]
    public async Task TheRightsAreStoredOnceEach_InCatalogOrder()
    {
        var editor = await RoleIdAsync("Editor");

        var stored = await _editor.SetRightsAsync(editor, [Rights.PostsCreate, Rights.AdminAccess, Rights.AdminAccess], "Admin");

        Assert.Equal("admin.access,posts.create", stored);
        Assert.Equal("admin.access,posts.create", await RightsOfAsync(editor));
    }

    [Fact]
    public async Task AnUnknownRightIsRefused_AndNothingIsStored_AndAnUnknownRoleIsNotFound()
    {
        var editor = await RoleIdAsync("Editor");
        var before = await RightsOfAsync(editor);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _editor.SetRightsAsync(editor, [Rights.AdminAccess, "everything.goes"], "Admin"));

        Assert.Equal(before, await RightsOfAsync(editor));
        Assert.Null(await _editor.SetRightsAsync("no-such-role", [Rights.AdminAccess], "Admin"));
    }

    [Fact]
    public async Task APersonCannotTakeAwayTheirOwnAbilityToManageUsersAndRoles()
    {
        var admin = await RoleIdAsync("Admin");
        var before = await RightsOfAsync(admin);

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => _editor.SetRightsAsync(admin, [Rights.AdminAccess, Rights.PostsRead], "Admin"));

        Assert.Contains("your own", refused.Message);
        Assert.Equal(before, await RightsOfAsync(admin));
    }

    [Fact]
    public async Task NoChangeMayLeaveNobodyAbleToManageUsersAndRoles()
    {
        var admin = await RoleIdAsync("Admin");

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => _editor.SetRightsAsync(admin, [Rights.AdminAccess], "Claude"));

        Assert.Contains("nobody", refused.Message);
        Assert.Equal("*", await RightsOfAsync(admin));
    }

    [Fact]
    public async Task AnotherHolderMakesTheChangePossible_AndTheLastHolderIsProtected()
    {
        var editor = await RoleIdAsync("Editor");
        var admin = await RoleIdAsync("Admin");
        await EditorPersonAsync("pat");
        await _editor.SetRightsAsync(editor, [Rights.AdminAccess, Rights.UsersManage, Rights.RolesManage], "Admin");

        await _editor.SetRightsAsync(admin, [Rights.AdminAccess, Rights.PostsRead], "pat"); // pat still manages through Editor
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => _editor.SetRightsAsync(editor, [Rights.AdminAccess], "pat"));

        Assert.Contains("your own", refused.Message);
        Assert.Equal("admin.access,users.manage,roles.manage", await RightsOfAsync(editor));
    }

    [Fact]
    public async Task WhenNobodyHoldsTheAbilityAlready_AnEditIsNotBlockedForever()
    {
        var roles = _services.GetRequiredService<IEntityRepository<Role>>();
        var admin = (await roles.GetAsync(await RoleIdAsync("Admin")))!;
        admin.Set("Rights", Guid.NewGuid().ToString(), Rights.AdminAccess); // the data state of a tenant run only by the sign-in role
        await roles.UpdateAsync(admin);

        var stored = await _editor.SetRightsAsync(await RoleIdAsync("Editor"), [Rights.AdminAccess, Rights.PostsRead], "Admin");

        Assert.Equal("admin.access,posts.read", stored);
    }

    [Fact]
    public async Task ARightsChangeLeavesAnAuditTrailForTheRole()
    {
        var editor = await RoleIdAsync("Editor");

        await _editor.SetRightsAsync(editor, [Rights.AdminAccess], "Admin");

        var store = _services.GetRequiredService<INQuadStore>();
        var subjects = (await store.QueryAsync(predicate: EntityConstants.AuditRecord.EntityIdPredicate, @object: editor)).Select(q => q.Subject);
        var to = new List<string>();
        foreach (var subject in subjects)
        {
            to.AddRange((await store.QueryAsync(subject: subject, predicate: EntityConstants.AuditRecord.ToValuePredicate)).Select(q => q.Object));
        }

        Assert.Contains("admin.access", to);
    }
}
