using Adventures.Entities;
using Adventures.Identity;
using Adventures.Identity.WebApi;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// Onboarding stage 2: on an empty store the first host becomes the first site: a tenant, its Admin (a person and a sign-in with the default password, forced to change at the first
/// sign-in) and the platform switch. It happens only when no tenant exists at all, only for a real host name, and only once however many requests arrive together.
/// Threat addressed: a populated install being taken over, or two first requests creating two sites.
/// </summary>
public sealed class FirstRunSiteTests
{
    private sealed class Tenants : ITenantAliasEditor, ITenantCreator
    {
        public List<TenantHostInfo> All { get; } = [];

        public int Creations { get; private set; }

        public Task<IReadOnlyList<TenantHostInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantHostInfo>>(All.ToList());

        public async Task<TenantHostInfo> CreateAsync(string name, string domain, CancellationToken cancellationToken = default)
        {
            Creations++;
            await Task.Delay(20, cancellationToken);   // a slow store, so two callers overlap
            var tenant = new TenantHostInfo("tenant-" + Creations, name, domain, []);
            All.Add(tenant);
            return tenant;
        }

        public Task<IReadOnlyList<string>?> AddAsync(string tenantId, string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>?> RemoveAsync(string tenantId, string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Accounts : IAccountCreator
    {
        public List<(string Tenant, string User, string Password, string[] Roles)> Created { get; } = [];

        public Task<bool> CreateIfMissingAsync(string tenant, string username, string password, IReadOnlyList<string> roles, string? email, string? displayName, CancellationToken cancellationToken = default)
        {
            Created.Add((tenant, username, password, roles.ToArray()));
            return Task.FromResult(true);
        }
    }

    private sealed class People : IUserBll
    {
        public List<(string Name, IReadOnlySet<string> Among)> Created { get; } = [];

        public List<string> CreatedIds { get; } = [];

        public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);

        public Task<User> CreateInTenantAsync(User entity, IReadOnlySet<string> tenantPeopleIds, CancellationToken cancellationToken = default)
        {
            Created.Add((entity.GetValue("UserName")?.ToString() ?? string.Empty, tenantPeopleIds));
            // like the real repository: a person without an Id is refused (the first run on a real store failed on exactly this, and a fake that gave the Id itself hid it)
            if (entity.EntityId is null)
            {
                throw new ArgumentException("Id is required.", nameof(entity));
            }

            CreatedIds.Add(entity.EntityId.Value);
            return Task.FromResult(entity);
        }

        public Task<User> CreateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User?> GetAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Membership : IUserMembership
    {
        public List<(string User, string Tenant)> Added { get; } = [];

        public Task<IReadOnlySet<string>> MemberIdsAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlySet<string>> TenantIdsOfAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task AddMemberAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
        {
            Added.Add((userId, tenantId));
            return Task.CompletedTask;
        }

        public Task<bool> OrganisationInTenantAsync(string organisationId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Platform : IFirstPlatformAdministrator
    {
        public int Calls { get; private set; }

        public Task<bool> EnsureFirstAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(true);
        }
    }

    private static readonly EntitySchema UserSchema = EntitySchema.Create(
        "urn:test:User",
        [new EntitySchemaField("urn:test:User", "f0", "Id", "String", "urn:p:id"), new EntitySchemaField("urn:test:User", "f1", "UserName", "String", "urn:p:name"), new EntitySchemaField("urn:test:User", "f2", "First", "String", "urn:p:first")],
        []);

    private (FirstRunSite Site, Tenants Tenants, Accounts Accounts, People People, Membership Membership, Platform Platform) Make()
    {
        var tenants = new Tenants();
        var accounts = new Accounts();
        var people = new People();
        var membership = new Membership();
        var platform = new Platform();
        return (new FirstRunSite(tenants, tenants, accounts, people, membership, platform, new EntitySchemaOf<User>(UserSchema)), tenants, accounts, people, membership, platform);
    }

    [Fact]
    public async Task OnAnEmptyStore_TheFirstHostBecomesTheFirstSite_WithItsOwnAdmin()
    {
        var run = Make();

        var done = await run.Site.TryCreateFirstSiteAsync("billkrat-001-site1.atempurl.com:443");

        Assert.True(done);
        var tenant = Assert.Single(run.Tenants.All);
        Assert.Equal(("billkrat-001-site1.atempurl.com", "billkrat-001-site1.atempurl.com"), (tenant.Name, tenant.Domain));
        Assert.Equal(("Admin", true), (Assert.Single(run.People.Created).Name, run.People.Created[0].Among.Count == 0));
        Assert.Equal((Assert.Single(run.People.CreatedIds), tenant.Id), Assert.Single(run.Membership.Added));
        var account = Assert.Single(run.Accounts.Created);
        Assert.Equal((tenant.Domain, "Admin", "Admin"), (account.Tenant, account.User, account.Password));
        Assert.Equal(["TenantAdmin"], account.Roles);
        Assert.Equal(1, run.Platform.Calls);
    }

    [Theory]
    [InlineData("api.foo.com", "foo.com")]
    [InlineData("www.foo.com", "foo.com")]
    [InlineData("mcp.foo.com:443", "foo.com")]
    [InlineData("foo.com", "foo.com")]
    [InlineData("api.foo.co.uk", "foo.co.uk")]
    [InlineData("shop.foo.com", "shop.foo.com")]
    [InlineData("api.localhost.test", "localhost.test")]
    public async Task TheSitesDomain_IsTheNameWithoutTheApiWwwOrMcpPrefix_BecauseTheApiLivesOnASubdomainOfTheSite(string host, string domain)
    {
        var run = Make();

        await run.Site.TryCreateFirstSiteAsync(host);

        Assert.Equal(domain, Assert.Single(run.Tenants.All).Domain);
        Assert.Equal(domain, Assert.Single(run.Accounts.Created).Tenant);
    }

    [Fact]
    public async Task WhenAnyTenantExists_NothingIsCreated_ARunningInstallIsNeverTakenOver()
    {
        var run = Make();
        run.Tenants.All.Add(new TenantHostInfo("t0", "Existing", "existing.example.org", []));

        Assert.False(await run.Site.TryCreateFirstSiteAsync("new.example.org"));

        Assert.Single(run.Tenants.All);
        Assert.Empty(run.Accounts.Created);
        Assert.Empty(run.People.Created);
        Assert.Equal(0, run.Platform.Calls);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("nodots")]
    [InlineData("not a host")]
    [InlineData("")]
    public async Task OnlyARealHostName_StartsASite(string host)
    {
        var run = Make();

        Assert.False(await run.Site.TryCreateFirstSiteAsync(host));

        Assert.Empty(run.Tenants.All);
        Assert.Empty(run.Accounts.Created);
    }

    [Fact]
    public async Task TwoFirstRequestsAtOnce_MakeExactlyOneSite()
    {
        var run = Make();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => run.Site.TryCreateFirstSiteAsync("first.example.org")));

        Assert.Equal(1, results.Count(r => r));
        Assert.Single(run.Tenants.All);
        Assert.Single(run.Accounts.Created);
        Assert.Equal(1, run.Tenants.Creations);
    }

    [Fact]
    public async Task ASecondHostOnTheNowPopulatedStore_IsNotASite()
    {
        var run = Make();
        await run.Site.TryCreateFirstSiteAsync("first.example.org");

        Assert.False(await run.Site.TryCreateFirstSiteAsync("second.example.org"));

        Assert.Single(run.Tenants.All);
    }
}
