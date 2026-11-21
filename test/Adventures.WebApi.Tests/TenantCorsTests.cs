using Adventures.Entities;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// T4a: the browser origins the API answers are the sites an administrator registered (each tenant's domain, its www host and its aliases, for example the
/// host's temporary address), read from the data and kept briefly, plus the fixed ones from configuration. Nothing else is trusted: no wildcard, no
/// look-alike name, no unregistered site. Which tenant a request is for is still decided only by the host the server saw; an allowed origin grants a browser
/// permission to call the API, never a tenant or a right.
/// </summary>
public sealed class TenantCorsTests
{
    private sealed class FakeHosts(params string[] names) : ITenantHostSource
    {
        public int Reads { get; private set; }

        public bool Throws { get; set; }

        public List<string> Names { get; } = [.. names];

        public Task<IReadOnlyList<string>> AllNamesAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Throws ? throw new InvalidOperationException("store down") : Task.FromResult<IReadOnlyList<string>>(Names.ToList());
        }
    }

    private static TenantCorsPolicyProvider Provider(FakeHosts hosts, string[]? fixedOrigins = null, int cacheSeconds = 60, TenantCacheInvalidator? invalidator = null)
    {
        var services = new ServiceCollection().AddSingleton<ITenantHostSource>(hosts).BuildServiceProvider();
        return new TenantCorsPolicyProvider(services.GetRequiredService<IServiceScopeFactory>(), new MemoryCache(new MemoryCacheOptions()), NullLogger<TenantCorsPolicyProvider>.Instance, "Angular", fixedOrigins ?? [], cacheSeconds, invalidator);
    }

    private static async Task<bool> AllowsAsync(TenantCorsPolicyProvider provider, string origin)
    {
        var policy = await provider.GetPolicyAsync(new DefaultHttpContext(), "Angular");
        return policy is not null && policy.IsOriginAllowed(origin);
    }

    [Fact]
    public async Task ARegisteredDomain_AllowsItsHttpsOrigin_AndItsWwwHost()
    {
        var provider = Provider(new FakeHosts("example.com"));

        Assert.True(await AllowsAsync(provider, "https://example.com"));
        Assert.True(await AllowsAsync(provider, "https://www.example.com"));
    }

    [Fact]
    public async Task AnAlias_IsAllowed_LikeTheDomain()
    {
        var provider = Provider(new FakeHosts("example.com", "other-brand.org"));

        Assert.True(await AllowsAsync(provider, "https://other-brand.org"));
        Assert.True(await AllowsAsync(provider, "https://www.other-brand.org"));
    }

    [Fact]
    public async Task ATemporaryHostAddress_IsAllowedOverPlainHttpToo_BecauseThatSiteHasNoCertificateYet()
    {
        var provider = Provider(new FakeHosts("example.com", "billkrat-001-site1.atempurl.com", "acct-site2.dtempurl.com"));

        Assert.True(await AllowsAsync(provider, "http://billkrat-001-site1.atempurl.com"));
        Assert.True(await AllowsAsync(provider, "https://billkrat-001-site1.atempurl.com"));
        Assert.True(await AllowsAsync(provider, "http://acct-site2.dtempurl.com"));
    }

    [Fact]
    public async Task ARealDomain_IsNeverAllowedOverPlainHttp()
    {
        var provider = Provider(new FakeHosts("example.com"));

        Assert.False(await AllowsAsync(provider, "http://example.com"));
        Assert.False(await AllowsAsync(provider, "http://www.example.com"));
    }

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("https://example.com.evil.com")]
    [InlineData("https://evilexample.com")]
    [InlineData("https://api.example.com")]
    [InlineData("https://example.com:8443")]
    [InlineData("https://EXAMPLE.com.attacker.test")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("ftp://example.com")]
    public async Task AnythingNotRegistered_IsRefused_NoWildcardNoLookAlike(string origin)
    {
        var provider = Provider(new FakeHosts("example.com"));

        Assert.False(await AllowsAsync(provider, origin));
    }

    [Fact]
    public async Task TheCaseOfTheOrigin_DoesNotMatter()
    {
        var provider = Provider(new FakeHosts("Example.COM"));

        Assert.True(await AllowsAsync(provider, "https://EXAMPLE.com"));
    }

    [Fact]
    public async Task TheFixedOriginsFromConfiguration_StillApply()
    {
        var provider = Provider(new FakeHosts(), ["http://localhost:4200", "https://configured.test"]);

        Assert.True(await AllowsAsync(provider, "http://localhost:4200"));
        Assert.True(await AllowsAsync(provider, "https://configured.test"));
        Assert.False(await AllowsAsync(provider, "http://localhost:9999"));
    }

    [Fact]
    public async Task AValueThatIsNotAHostName_IsIgnored_NotTurnedIntoATrustedOrigin()
    {
        var provider = Provider(new FakeHosts("https://example.com/path", "*.example.com", "exa mple.com", "", "  ", "example.com:8443"));

        Assert.False(await AllowsAsync(provider, "https://*.example.com"));
        Assert.False(await AllowsAsync(provider, "https://https://example.com/path"));
        Assert.False(await AllowsAsync(provider, "https://example.com"));
    }

    [Fact]
    public async Task TheListIsKept_SoTheStoreIsNotReadOnEveryRequest_AndAnAddedSiteIsSeenWhenTheKeepingEnds()
    {
        var hosts = new FakeHosts("example.com");
        var provider = Provider(hosts, cacheSeconds: 60);

        await AllowsAsync(provider, "https://example.com");
        await AllowsAsync(provider, "https://example.com");
        await AllowsAsync(provider, "https://new.test");

        Assert.Equal(1, hosts.Reads);

        var uncached = Provider(hosts, cacheSeconds: 0);
        hosts.Names.Add("new.test");
        Assert.True(await AllowsAsync(uncached, "https://new.test"));
    }

    [Fact]
    public async Task SavingAHostName_ClearsTheKeptList_SoTheNewSiteWorksAtOnce()
    {
        var hosts = new FakeHosts("example.com");
        var invalidator = new TenantCacheInvalidator();
        var provider = Provider(hosts, cacheSeconds: 600, invalidator: invalidator);
        Assert.False(await AllowsAsync(provider, "https://new.test"));

        hosts.Names.Add("new.test");
        Assert.False(await AllowsAsync(provider, "https://new.test")); // still kept
        invalidator.Invalidate();

        Assert.True(await AllowsAsync(provider, "https://new.test"));
    }

    [Fact]
    public async Task IfTheStoreCannotBeRead_OnlyTheFixedOriginsApply_AndNothingThrows()
    {
        var hosts = new FakeHosts("example.com") { Throws = true };
        var provider = Provider(hosts, ["https://configured.test"]);

        Assert.False(await AllowsAsync(provider, "https://example.com"));
        Assert.True(await AllowsAsync(provider, "https://configured.test"));
    }

    [Fact]
    public async Task OtherPolicyNames_GetNothing_AndThePolicyAllowsNoCookies()
    {
        var provider = Provider(new FakeHosts("example.com"));

        Assert.Null(await provider.GetPolicyAsync(new DefaultHttpContext(), "something-else"));
        var policy = await provider.GetPolicyAsync(new DefaultHttpContext(), "Angular");
        Assert.False(policy!.SupportsCredentials);
        Assert.False(policy.AllowAnyOrigin);
    }

    [Fact]
    public async Task ThePolicyKeepsTheHeadersAndMethodsTheAppNeeds_AndExposesTheCorrelationId()
    {
        var provider = Provider(new FakeHosts("example.com"));

        var policy = (await provider.GetPolicyAsync(new DefaultHttpContext(), "Angular"))!;

        Assert.True(policy.AllowAnyHeader);
        Assert.True(policy.AllowAnyMethod);
        Assert.Contains(CorrelationIdMiddleware.HeaderName, policy.ExposedHeaders);
    }
}
