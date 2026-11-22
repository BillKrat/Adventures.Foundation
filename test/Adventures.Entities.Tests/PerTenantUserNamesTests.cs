using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// Onboarding stage 1 (decision 2026-11-tenant-onboarding): a user name is unique within a tenant, not across the system, so every tenant can have its own Admin. The sign-in
/// account is already per tenant; what had to change is the person behind it: with two people called Admin, whoever is looked up by name for a request is the one who belongs to the
/// tenant the request came on, never a guess. Threat addressed: one tenant's Admin being taken for another's (rights, profile, switcher).
/// </summary>
public sealed class PerTenantUserNamesTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private sealed class FakeMembership : IUserMembership
    {
        public Dictionary<string, HashSet<string>> Members { get; } = [];

        public Task<IReadOnlySet<string>> MemberIdsAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(Members.TryGetValue(tenantId, out var ids) ? ids.ToHashSet() : []);

        public Task<IReadOnlySet<string>> TenantIdsOfAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(Members.Where(m => m.Value.Contains(userId)).Select(m => m.Key).ToHashSet());

        public Task AddMemberAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
        {
            if (!Members.TryGetValue(tenantId, out var ids))
            {
                Members[tenantId] = ids = [];
            }

            ids.Add(userId);
            return Task.CompletedTask;
        }

        public Task<bool> OrganisationInTenantAsync(string organisationId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Home(string? tenantId) : IHomeTenant
    {
        public string? HomeTenantId { get; set; } = tenantId;
    }

    private FakeMembership _membership = null!;
    private Home _home = null!;
    private UserBll _bll = null!;
    private EntitySchema _schema = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq"));
        _schema = SchemaDal.Load(await store.QueryAsync(), UserConstants.SchemaIri);
        var repository = new NQuadEntityRepository<User>(store, _schema, UserConstants.BaseIri, UserConstants.TypeIri, Graph, s => new User(s));
        _membership = new FakeMembership();
        _home = new Home(null);
        _bll = new UserBll(new EntityDal<User>(repository), _membership, _home);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private User Person(string userName)
    {
        var id = Guid.NewGuid().ToString();
        return (User)new User(_schema)
            .Set(DynamicEntity.EntityIdPropertyName, id, id)
            .Set("UserName", Guid.NewGuid().ToString(), userName)
            .Set("First", Guid.NewGuid().ToString(), userName);
    }

    /// <summary>What the tenant people screen does: a person joins the tenant when added, and the name is checked among that tenant's people only.</summary>
    private async Task<User> AddToTenantAsync(string tenantId, string userName)
    {
        var members = await _membership.MemberIdsAsync(tenantId);
        var created = await _bll.CreateInTenantAsync(Person(userName), members);
        await _membership.AddMemberAsync(created.EntityId!.Value, tenantId);
        return created;
    }

    [Fact]
    public async Task TwoTenants_CanEachHaveTheirOwnAdmin()
    {
        var a = await AddToTenantAsync("tenant-a", "Owner");
        var b = await AddToTenantAsync("tenant-b", "Owner");

        Assert.NotEqual(a.EntityId, b.EntityId);
        Assert.Equal(2, (await _bll.ListAsync()).Count(u => string.Equals(u.GetValue("UserName") as string, "Owner", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task WithinOneTenant_ANameIsStillUnique_IgnoringCase()
    {
        await AddToTenantAsync("tenant-a", "Owner");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => AddToTenantAsync("tenant-a", "OWNER"));

        Assert.Contains("taken", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheGlobalCreate_KeepsTheOldSystemWideRule()
    {
        await _bll.CreateAsync(Person("Solo"));

        await Assert.ThrowsAsync<BusinessRuleException>(() => _bll.CreateAsync(Person("solo")));
    }

    [Fact]
    public async Task ARequestOnATenant_GetsThatTenantsPerson_NotTheOtherOne()
    {
        var a = await AddToTenantAsync("tenant-a", "Owner");
        var b = await AddToTenantAsync("tenant-b", "Owner");

        _home.HomeTenantId = "tenant-a";
        Assert.Equal(a.EntityId, (await _bll.FindByUserNameAsync("owner"))!.EntityId);
        _home.HomeTenantId = "tenant-b";
        Assert.Equal(b.EntityId, (await _bll.FindByUserNameAsync("OWNER"))!.EntityId);
    }

    [Fact]
    public async Task WhenSeveralPeopleShareTheNameAndTheRequestsTenantHasNone_NobodyIsPicked()
    {
        await AddToTenantAsync("tenant-a", "Owner");
        await AddToTenantAsync("tenant-b", "Owner");

        _home.HomeTenantId = "tenant-c";
        Assert.Null(await _bll.FindByUserNameAsync("Owner"));
        _home.HomeTenantId = null;
        Assert.Null(await _bll.FindByUserNameAsync("Owner"));   // no tenant to decide by: never a guess
    }

    [Fact]
    public async Task OneOwnerOfTheName_IsFoundAsBefore_WhateverTheTenant()
    {
        var only = await _bll.CreateAsync(Person("Solo"));

        _home.HomeTenantId = null;
        Assert.Equal(only.EntityId, (await _bll.FindByUserNameAsync("solo"))!.EntityId);
        _home.HomeTenantId = "any-tenant";
        Assert.Equal(only.EntityId, (await _bll.FindByUserNameAsync("Solo"))!.EntityId);
    }

    [Fact]
    public async Task APersonOfTwoTenants_IsStillOnePerson_FoundInBoth()
    {
        var shared = await AddToTenantAsync("tenant-a", "Shared");
        await _membership.AddMemberAsync(shared.EntityId!.Value, "tenant-b");

        _home.HomeTenantId = "tenant-b";

        Assert.Equal(shared.EntityId, (await _bll.FindByUserNameAsync("Shared"))!.EntityId);
    }

    [Fact]
    public async Task TheTenantPeopleScreen_AddsEachTenantsOwnOwner_AndChecksOnlyItsOwnPeople()
    {
        var tenantA = new TenantScopedUserBll(_bll, _membership, new FixedTenant("tenant-a"));
        var tenantB = new TenantScopedUserBll(_bll, _membership, new FixedTenant("tenant-b"));

        var a = await tenantA.CreateAsync(Person("Owner"));
        var b = await tenantB.CreateAsync(Person("Owner"));

        Assert.NotEqual(a.EntityId, b.EntityId);
        Assert.Contains(a.EntityId!.Value, await _membership.MemberIdsAsync("tenant-a"));
        Assert.DoesNotContain(a.EntityId!.Value, await _membership.MemberIdsAsync("tenant-b"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => tenantA.CreateAsync(Person("owner")));
    }

    private sealed class FixedTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
    }
}
