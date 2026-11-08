using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// The platform administrator switch (2026-11-08, replaces the Platform role of T3): a switch on the person, not tied to a tenant, never granted by a role, only changed by
/// someone who already has it, never left empty, and given to Admin automatically when nobody has it. Threat addressed: a tenant administrator granting themselves platform power.
/// </summary>
public sealed class PlatformAdministratorTests : IAsyncLifetime
{
    private const string DevMain = "a2b7af6d-4c2b-4732-b837-1e6d49b2c05d";
    private const string OtherTenant = "92296cae-0b8f-448c-8ae2-d8a26234ccf9";
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
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.OrganizationSchemaIri), EntityConstants.Identity.OrganizationBaseIri, EntityConstants.Identity.OrganizationTypeIri, graph, s => new Organization(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.TenantRoleSchemaIri), EntityConstants.Identity.TenantRoleBaseIri, EntityConstants.Identity.TenantRoleTypeIri, graph, s => new TenantRoleAssignment(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.OrganizationRoleSchemaIri), EntityConstants.Identity.OrganizationRoleBaseIri, EntityConstants.Identity.OrganizationRoleTypeIri, graph, s => new OrganizationRoleAssignment(s));
        services.AddNQuadRelations();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IEntityBll<Role>>(p => new EntityBll<Role>(new EntityDal<Role>(p.GetRequiredService<IEntityRepository<Role>>())));
        services.AddScoped<IEntityBll<Organization>>(p => new EntityBll<Organization>(new EntityDal<Organization>(p.GetRequiredService<IEntityRepository<Organization>>())));
        services.AddScoped<IRightsResolver, RightsResolver>();
        services.AddScoped<IRoleRightsEditor, RoleRightsEditor>();
        services.AddNQuadPlatformAdministrators();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> IdOfAsync(string userName) =>
        (await _services.GetRequiredService<IUserBll>().FindByUserNameAsync(userName))!.EntityId!.Value;

    [Fact]
    public async Task NobodyIsAPlatformAdministratorUntilSwitchedOn()
    {
        Assert.Empty(await _services.GetRequiredService<IPlatformAdministrators>().AllAsync());
        Assert.DoesNotContain(Rights.PlatformManage, await _services.GetRequiredService<IRightsResolver>().ResolveAsync("Admin", ["TenantAdmin"], DevMain));
    }

    [Fact]
    public async Task TheSwitchGivesThePlatformRightInEveryTenant_AndTheLegacyAdministratorKeepsEveryTenantRightToo()
    {
        var resolver = _services.GetRequiredService<IRightsResolver>();
        var bll = _services.GetRequiredService<PlatformAdministratorBll>();
        Assert.True(await bll.SetAsync(await IdOfAsync("Admin"), true, "test"));
        Assert.True(await bll.SetAsync(await IdOfAsync("Claude"), true, "test"));

        foreach (var tenant in new[] { DevMain, OtherTenant })
        {
            Assert.Contains(Rights.PlatformManage, await resolver.ResolveAsync("Claude", [], tenant));
            var legacy = await resolver.ResolveAsync("Admin", ["TenantAdmin"], tenant);
            Assert.Contains(Rights.PlatformManage, legacy);
            Assert.True(Rights.AllNames.IsSubsetOf(legacy));
        }
    }

    [Fact]
    public async Task APlatformAdministratorDoesNotSilentlyGetEveryTenantRight()
    {
        // Platform means "manage the platform" (roles, tenants); content and people rights in a tenant still come from a role held there.
        var bll = _services.GetRequiredService<PlatformAdministratorBll>();
        await bll.SetAsync(await IdOfAsync("Claude"), true, "test");

        var rights = await _services.GetRequiredService<IRightsResolver>().ResolveAsync("Claude", [], "00000000-0000-0000-0000-000000000000");
        Assert.Equal([Rights.PlatformManage], rights);
    }

    [Fact]
    public async Task SwitchingOffTakesThePlatformRightAway_AndTheLastAdministratorCannotBeSwitchedOff()
    {
        var bll = _services.GetRequiredService<PlatformAdministratorBll>();
        var admin = await IdOfAsync("Admin");
        var claude = await IdOfAsync("Claude");
        await bll.SetAsync(admin, true, "test");
        await bll.SetAsync(claude, true, "test");

        await bll.SetAsync(claude, false, "Admin");
        Assert.DoesNotContain(Rights.PlatformManage, await _services.GetRequiredService<IRightsResolver>().ResolveAsync("Claude", [], DevMain));

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => bll.SetAsync(admin, false, "Admin"));
        Assert.Contains("last platform administrator", refusal.Message);
        Assert.Contains(admin, await _services.GetRequiredService<IPlatformAdministrators>().AllAsync());
    }

    [Fact]
    public async Task ASwitchForAPersonWhoDoesNotExistChangesNothing()
    {
        var bll = _services.GetRequiredService<PlatformAdministratorBll>();
        Assert.False(await bll.SetAsync(Guid.NewGuid().ToString(), true, "test"));
        Assert.Empty(await _services.GetRequiredService<IPlatformAdministrators>().AllAsync());
    }

    [Fact]
    public async Task AdminGetsTheSwitchOnlyWhenNobodyHasIt_AndOnlyOnce()
    {
        var bll = _services.GetRequiredService<PlatformAdministratorBll>();
        var store = _services.GetRequiredService<IPlatformAdministrators>();
        Assert.True(await bll.EnsureFirstAsync());
        Assert.Equal([await IdOfAsync("Admin")], await store.AllAsync());
        Assert.False(await bll.EnsureFirstAsync());

        // someone else holds it and Admin was switched off on purpose: startup does not give it back
        await bll.SetAsync(await IdOfAsync("Claude"), true, "Admin");
        await bll.SetAsync(await IdOfAsync("Admin"), false, "Claude");
        Assert.False(await bll.EnsureFirstAsync());
        Assert.DoesNotContain(await IdOfAsync("Admin"), await store.AllAsync());
    }

    [Fact]
    public async Task ARoleCanNeverHoldThePlatformRight()
    {
        Assert.DoesNotContain(Rights.PlatformManage, Rights.Parse(Rights.PlatformManage));
        Assert.DoesNotContain(Rights.PlatformManage, Rights.Parse("*"));
        Assert.DoesNotContain(Rights.PlatformManage, Rights.AllNames);

        var roles = await _services.GetRequiredService<IEntityRepository<Role>>().ListAsync();
        var editor = roles.Single(role => (string)role.GetValue("Name")! == "Editor").EntityId!.Value;
        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _services.GetRequiredService<IRoleRightsEditor>().SetRightsAsync(editor, [Rights.PostsRead, Rights.PlatformManage], "Admin"));
        Assert.Contains("switch on the person", refusal.Message);
    }

    [Fact]
    public async Task TheLatestStatementWins_NotTheOrderTheyAreRead()
    {
        var store = _services.GetRequiredService<INQuadStore>();
        var admin = await IdOfAsync("Admin");
        var subject = EntityConstants.User.BaseIri + admin;
        await store.InsertAsync(new NQuad(Guid.NewGuid(), subject, NQuadPlatformAdministrators.Predicate, "200;false;test", NQuadPlatformAdministrators.Graph));
        await store.InsertAsync(new NQuad(Guid.NewGuid(), subject, NQuadPlatformAdministrators.Predicate, "100;true;test", NQuadPlatformAdministrators.Graph));
        await store.InsertAsync(new NQuad(Guid.NewGuid(), subject, NQuadPlatformAdministrators.Predicate, "garbage", NQuadPlatformAdministrators.Graph));

        Assert.Empty(await _services.GetRequiredService<IPlatformAdministrators>().AllAsync());
    }
}
