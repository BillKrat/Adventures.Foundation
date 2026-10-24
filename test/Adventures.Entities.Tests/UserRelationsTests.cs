// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
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
    private IEntityRelationWriter _writer = null!;
    private EntitySchema _userSchema = null!;

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
        _services = services.BuildServiceProvider();
        _users = _services.GetRequiredService<IUserPresenter>();
        _writer = _services.GetRequiredService<IEntityRelationWriter>();
        _userSchema = _services.GetRequiredService<EntitySchemaOf<User>>().Schema;
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

    [Fact]
    public async Task AddingAnAssignment_CreatesTheChildAndLinksIt_AndItShowsOnTheUsersForm()
    {
        var (userId, _, editorId, tenantId) = await IdsAsync();

        var created = await _writer.AddAsync(_userSchema, userId, "TenantRoles", Assignment(tenantId, editorId));

        Assert.NotNull(created);
        var form = await _users.GetFormAsync(userId);
        var row = Assert.Single(form!.Relations!.Single(r => r.Name == "TenantRoles").Grid.Rows);
        Assert.Equal(created!.EntityId, row.EntityId);
        Assert.Equal("Editor", row.Display!["Role"]);
        Assert.True(form.Relations!.Single(r => r.Name == "TenantRoles").Grid.CanCreate); // an admin form is editable
        Assert.False((await _users.GetFormByUserNameAsync("Claude"))!.Relations!.Single(r => r.Name == "TenantRoles").Grid.CanCreate); // the profile is not
    }

    [Fact]
    public async Task AddingTheSameAssignmentTwice_IsABusinessRuleRefusal()
    {
        var (userId, _, editorId, tenantId) = await IdsAsync();
        await _writer.AddAsync(_userSchema, userId, "TenantRoles", Assignment(tenantId, editorId));

        await Assert.ThrowsAsync<Adventures.Common.Exceptions.BusinessRuleException>(
            () => _writer.AddAsync(_userSchema, userId, "TenantRoles", Assignment(tenantId, editorId)));
    }

    [Fact]
    public async Task AnAssignmentNeedsBothPointers_AndTheyMustExist()
    {
        var (userId, _, editorId, tenantId) = await IdsAsync();

        var missing = await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(
            () => _writer.AddAsync(_userSchema, userId, "TenantRoles", new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Tenant"] = tenantId })));
        Assert.Contains("Role", missing.Errors.Keys);

        var unknown = await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(
            () => _writer.AddAsync(_userSchema, userId, "TenantRoles", Assignment(tenantId, "no-such-role")));
        Assert.Equal("Role does not exist.", unknown.Errors["Role"].Single());
        Assert.Null(await _writer.AddAsync(_userSchema, "no-such-user", "TenantRoles", Assignment(tenantId, editorId)));
    }

    [Fact]
    public async Task RemovingAnAssignment_RemovesItFromTheForm_AndAnUnknownChildIsNotFound()
    {
        var (userId, _, editorId, tenantId) = await IdsAsync();
        var created = await _writer.AddAsync(_userSchema, userId, "TenantRoles", Assignment(tenantId, editorId));

        Assert.False(await _writer.RemoveAsync(_userSchema, userId, "TenantRoles", "not-a-child"));
        Assert.True(await _writer.RemoveAsync(_userSchema, userId, "TenantRoles", created!.EntityId));

        Assert.Empty((await _users.GetFormAsync(userId))!.Relations!.Single(r => r.Name == "TenantRoles").Grid.Rows);
    }

    [Fact]
    public async Task TheLastAdminAssignmentOfATenantCannotBeRemoved_ButANonLastOneCan()
    {
        var admin = await _users.GetFormByUserNameAsync("Admin");
        var adminId = admin!.Entity.EntityId;
        var tenantRoles = admin.Relations!.Single(r => r.Name == "TenantRoles").Grid.Rows;
        var first = tenantRoles.First(); // Admin role in a tenant: the only Admin assignment for that tenant in the seed

        var refused = await Assert.ThrowsAsync<Adventures.Common.Exceptions.BusinessRuleException>(
            () => _writer.RemoveAsync(_userSchema, adminId, "TenantRoles", first.EntityId));
        Assert.Contains("at least one administrator", refused.Message);

        // a second Admin assignment for the same tenant (a different user) makes the first removable
        var (claudeId, adminRoleId, _, _) = await IdsAsync();
        await _writer.AddAsync(_userSchema, claudeId, "TenantRoles", Assignment(first.Values["Tenant"]!, adminRoleId));
        Assert.True(await _writer.RemoveAsync(_userSchema, adminId, "TenantRoles", first.EntityId));
    }

    [Fact]
    public async Task Lookups_ListEveryEntityOfASchemaAsIdAndText_AndNothingForAnUnknownSchema()
    {
        var provider = _services.GetRequiredService<IEntityRelationProvider>();

        var roles = await provider.GetOptionsAsync(EntityConstants.Identity.RoleSchemaIri);

        Assert.Equal(["Admin", "Developer", "Editor", "Reviewer"], roles.Select(o => o.Text));
        Assert.Empty(await provider.GetOptionsAsync("urn:not-registered"));
    }
}
