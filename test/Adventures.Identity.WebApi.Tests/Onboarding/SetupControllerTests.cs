using System.Reflection;
using System.Security.Claims;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.Identity;
using Adventures.Identity.WebApi;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// Onboarding stage 3: a host that reaches no tenant shows a setup page. A platform administrator of an existing site signs in there (with the site's own sign-in, never as a person of
/// another tenant) and chooses once: attach the host to an existing site as an alias, or start a new site with its own Admin. The setup token works only for this API, only while the host
/// still reaches no tenant, and only while the person is still a platform administrator. Threats addressed: anyone adding a host to a tenant, a setup token used elsewhere, the site list
/// leaking before sign-in, a password guessed through the setup sign-in.
/// </summary>
public sealed class SetupControllerTests
{
    private sealed class Tenants : ITenantAliasEditor
    {
        public List<TenantHostInfo> All { get; } = [new("t-main", "Main", "main.example.org", []), new("t-other", "Other", "other.example.org", [])];

        public List<(string TenantId, string Host)> AliasesAdded { get; } = [];

        public Task<IReadOnlyList<TenantHostInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantHostInfo>>(All.ToList());

        public Task<IReadOnlyList<string>?> AddAsync(string tenantId, string host, CancellationToken cancellationToken = default)
        {
            if (All.All(t => t.Id != tenantId))
            {
                return Task.FromResult<IReadOnlyList<string>?>(null);
            }

            AliasesAdded.Add((tenantId, host));
            return Task.FromResult<IReadOnlyList<string>?>([host]);
        }

        public Task<IReadOnlyList<string>?> RemoveAsync(string tenantId, string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Resolver(bool reaches) : ITenantResolver
    {
        public Task<CurrentTenant> ResolveAsync(string? host, string? fallbackDomain = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(reaches ? new CurrentTenant("t-main", "Main", "main.example.org", host ?? string.Empty, TenantMatch.Domain) : new CurrentTenant(null, null, null, host ?? string.Empty, TenantMatch.None));
    }

    private sealed class Accounts(LoginResult result) : IUserAccountService
    {
        public List<(string Tenant, string User)> Attempts { get; } = [];

        public Task<LoginResult> LoginAsync(string tenant, string username, string password, CancellationToken cancellationToken = default)
        {
            Attempts.Add((tenant, username));
            return Task.FromResult(password == "right" ? result : LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        }

        public Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Membership : IUserMembership
    {
        public Task<IReadOnlySet<string>> MemberIdsAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(tenantId == "t-main" ? new HashSet<string> { "person-admin", "person-editor" } : new HashSet<string> { "person-other-admin" });

        public Task<IReadOnlySet<string>> TenantIdsOfAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task AddMemberAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> OrganisationInTenantAsync(string organisationId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class People : IUserBll
    {
        private readonly List<User> _people;

        public People()
        {
            _people = [Person("person-admin", "Admin"), Person("person-editor", "editor"), Person("person-other-admin", "Admin")];
        }

        private static User Person(string id, string name) =>
            (User)new User(Schema).Set(DynamicEntity.EntityIdPropertyName, id, id).Set("UserName", Guid.NewGuid().ToString(), name);

        public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>(_people);

        public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> CreateInTenantAsync(User entity, IReadOnlySet<string> tenantPeopleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> CreateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User?> GetAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Platform(params string[] ids) : IPlatformAdministrators
    {
        public HashSet<string> On { get; } = [.. ids];

        public Task<bool> IsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(On.Contains(userId));

        public Task<IReadOnlySet<string>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(On.ToHashSet());

        public Task SetAsync(string userId, bool on, string changedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Tokens : IJwtTokenService
    {
        public List<(string Subject, Claim[] Claims)> Issued { get; } = [];

        public IssuedToken IssueToken(string subject, IEnumerable<Claim>? additionalClaims = null)
        {
            Issued.Add((subject, (additionalClaims ?? []).ToArray()));
            return new IssuedToken("setup-token", DateTimeOffset.UtcNow.AddMinutes(20));
        }

        public IssuedToken IssueClientToken(string clientId, IEnumerable<string> scopes) => throw new NotSupportedException();
    }

    private sealed class Provisioner : ISiteProvisioner
    {
        public List<string> Created { get; } = [];

        public bool CreatesAccounts => true;

        public Task<TenantHostInfo> CreateAsync(string domain, string name, CancellationToken cancellationToken = default)
        {
            Created.Add(domain + "|" + name);
            return Task.FromResult(new TenantHostInfo("t-new", name, domain, []));
        }
    }

    private static readonly EntitySchema Schema = EntitySchema.Create(
        "urn:test:User",
        [new EntitySchemaField("urn:test:User", "f0", "Id", "String", "urn:p:id"), new EntitySchemaField("urn:test:User", "f1", "UserName", "String", "urn:p:name")],
        []);

    private sealed class Rig
    {
        public Tenants Tenants { get; } = new();
        public Accounts Accounts { get; init; } = new(LoginResult.Success("normal-token", DateTimeOffset.UtcNow.AddMinutes(30), false));
        public Platform Platform { get; init; } = new("person-admin");
        public Tokens Tokens { get; } = new();
        public Provisioner Provisioner { get; } = new();
        public TenantCacheInvalidator Caches { get; } = new();
        public bool HostReachesATenant { get; init; }

        public SetupController Controller(string host = "unknown.example.org", ClaimsPrincipal? user = null) =>
            new(Tenants, new Resolver(HostReachesATenant), Accounts, new People(), new Membership(), Platform, Tokens, Provisioner, Caches)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()), Request = { Host = new HostString(host) } } },
            };

        public ClaimsPrincipal SetupUser(string person = "person-admin") =>
            new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "setup-session"), new Claim(WellKnownClaims.Setup, "platform"), new Claim(WellKnownClaims.SetupPerson, person)], "test"));
    }

    // ---- status

    [Fact]
    public async Task TheStatus_SaysWhetherThisHostNeedsSetup_AndNamesNoSite()
    {
        var unknown = Assert.IsType<OkObjectResult>(await new Rig().Controller("unknown.example.org").Status(CancellationToken.None));
        var known = Assert.IsType<OkObjectResult>(await new Rig { HostReachesATenant = true }.Controller("main.example.org").Status(CancellationToken.None));

        var needs = Assert.IsType<SetupStatus>(unknown.Value);
        Assert.True(needs.NeedsSetup);
        Assert.Equal("unknown.example.org", needs.Host);
        Assert.False(Assert.IsType<SetupStatus>(known.Value).NeedsSetup);
        Assert.DoesNotContain(typeof(SetupStatus).GetProperties(), p => p.PropertyType.IsGenericType);   // no lists of sites
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("nodots")]
    public async Task AHostThatCannotBeASite_NeverNeedsSetup(string host)
    {
        var result = Assert.IsType<OkObjectResult>(await new Rig().Controller(host).Status(CancellationToken.None));

        Assert.False(Assert.IsType<SetupStatus>(result.Value).NeedsSetup);
    }

    [Fact]
    public async Task WhenNoTenantExistsAtAll_ThereIsNothingToAttachTo_SoTheFirstRunHandlesIt()
    {
        var rig = new Rig();
        rig.Tenants.All.Clear();

        var result = Assert.IsType<OkObjectResult>(await rig.Controller().Status(CancellationToken.None));

        Assert.False(Assert.IsType<SetupStatus>(result.Value).NeedsSetup);
    }

    // ---- the sign-in

    [Fact]
    public async Task ThePlatformAdministratorOfASite_GetsASetupToken_WithNoTenantNoRolesAndNoRealName()
    {
        var rig = new Rig();

        var result = await rig.Controller().Token(new SetupSignIn("main.example.org", "Admin", "right"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("setup-token", Assert.IsType<SetupToken>(ok.Value).AccessToken);
        var issued = Assert.Single(rig.Tokens.Issued);
        var claims = issued.Claims.ToDictionary(c => c.Type, c => c.Value);
        Assert.Equal("platform", claims[WellKnownClaims.Setup]);
        Assert.Equal("person-admin", claims[WellKnownClaims.SetupPerson]);
        Assert.Equal("setup-session", claims[ClaimTypes.Name]);
        Assert.DoesNotContain(WellKnownClaims.Tenant, claims.Keys);
        Assert.DoesNotContain(WellKnownClaims.Roles, claims.Keys);
        Assert.Equal(("main.example.org", "Admin"), Assert.Single(rig.Accounts.Attempts));
    }

    [Theory]
    [InlineData("main.example.org", "Admin", "wrong")]
    [InlineData("main.example.org", "editor", "right")]
    [InlineData("no-such-site.example.org", "Admin", "right")]
    [InlineData("other.example.org", "Admin", "right")]
    [InlineData("main.example.org", "ghost", "right")]
    public async Task EveryFailure_LooksTheSame_AWrongPasswordANonAdministratorAnUnknownSiteOrPerson(string site, string user, string password)
    {
        var rig = new Rig();

        var result = await rig.Controller().Token(new SetupSignIn(site, user, password), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(rig.Tokens.Issued);
    }

    [Fact]
    public async Task APasswordThatMustBeChangedFirst_IsNotEnoughToSetUp()
    {
        var rig = new Rig { Accounts = new Accounts(LoginResult.Success("normal-token", DateTimeOffset.UtcNow.AddMinutes(30), mustChangePassword: true)) };

        var result = await rig.Controller().Token(new SetupSignIn("main.example.org", "Admin", "right"), CancellationToken.None);

        Assert.IsNotType<OkObjectResult>(result);
        Assert.Empty(rig.Tokens.Issued);
    }

    [Fact]
    public async Task OnAHostThatReachesATenant_ThereIsNoSetup()
    {
        var rig = new Rig { HostReachesATenant = true };

        Assert.IsType<NotFoundResult>(await rig.Controller("main.example.org").Token(new SetupSignIn("main.example.org", "Admin", "right"), CancellationToken.None));
        Assert.Empty(rig.Accounts.Attempts);
    }

    [Fact]
    public async Task ASetupSignIn_IsRateLimited_LikeEveryOtherSignIn()
    {
        var token = typeof(SetupController).GetMethod(nameof(SetupController.Token))!;

        Assert.Equal(RequestHardening.TokenPolicy, token.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!.PolicyName);
        await Task.CompletedTask;
    }

    // ---- the choices

    [Fact]
    public async Task TheSiteList_IsForASetupTokenOnly_ThatStillBelongsToAPlatformAdministrator()
    {
        var rig = new Rig();

        Assert.IsType<ForbidResult>(await rig.Controller(user: new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin")], "test"))).Sites(CancellationToken.None));
        Assert.IsType<ForbidResult>(await rig.Controller(user: new ClaimsPrincipal(new ClaimsIdentity())).Sites(CancellationToken.None));
        var sites = Assert.IsAssignableFrom<IReadOnlyList<SetupSite>>(Assert.IsType<OkObjectResult>(await rig.Controller(user: rig.SetupUser()).Sites(CancellationToken.None)).Value);
        Assert.Equal(["main.example.org", "other.example.org"], sites.Select(s => s.Domain));

        rig.Platform.On.Clear();   // switched off since the token was issued
        Assert.IsType<ForbidResult>(await rig.Controller(user: rig.SetupUser()).Sites(CancellationToken.None));
    }

    [Fact]
    public async Task AttachingTheHost_MakesItAnAliasOfTheChosenSite_AndClearsTheCaches()
    {
        var rig = new Rig();
        var before = rig.Caches.Token;

        var result = await rig.Controller("unknown.example.org", rig.SetupUser()).Attach(new SetupAttach("t-other"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(("t-other", "unknown.example.org"), Assert.Single(rig.Tenants.AliasesAdded));
        Assert.True(before.HasChanged);
    }

    [Fact]
    public async Task AttachingToASiteThatDoesNotExist_IsNotFound_AndWithoutASetupTokenIsRefused()
    {
        var rig = new Rig();

        Assert.IsType<NotFoundResult>(await rig.Controller(user: rig.SetupUser()).Attach(new SetupAttach("nope"), CancellationToken.None));
        Assert.IsType<ForbidResult>(await rig.Controller().Attach(new SetupAttach("t-main"), CancellationToken.None));
        Assert.Empty(rig.Tenants.AliasesAdded);
    }

    [Fact]
    public async Task StartingANewSite_UsesTheHostsSiteDomain_AndTheGivenName()
    {
        var rig = new Rig();

        var result = await rig.Controller("api.fresh.example.org", rig.SetupUser()).NewSite(new SetupNewSite("Fresh Site"), CancellationToken.None);

        var created = Assert.IsType<TenantHostInfo>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(("fresh.example.org", "Fresh Site"), (created.Domain, created.Name));
        Assert.Equal("fresh.example.org|Fresh Site", Assert.Single(rig.Provisioner.Created));
    }

    [Fact]
    public async Task EveryChoice_IsRefusedOnAHostThatAlreadyReachesATenant_AndWithoutASetupToken()
    {
        var rig = new Rig { HostReachesATenant = true };
        var controller = rig.Controller("main.example.org", rig.SetupUser());

        Assert.IsType<NotFoundResult>(await controller.Sites(CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Attach(new SetupAttach("t-other"), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.NewSite(new SetupNewSite("X"), CancellationToken.None));
        Assert.Empty(rig.Provisioner.Created);
    }

    [Fact]
    public void OnlyTheStatusAndTheSignIn_AreAnonymous_EverythingElseNeedsASignedInCaller()
    {
        var methods = typeof(SetupController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var anonymous = methods.Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any()).Select(m => m.Name).Order().ToArray();
        Assert.Equal([nameof(SetupController.Status), nameof(SetupController.Token)], anonymous);
        Assert.NotEmpty(typeof(SetupController).GetCustomAttributes<AuthorizeAttribute>());
    }
}
