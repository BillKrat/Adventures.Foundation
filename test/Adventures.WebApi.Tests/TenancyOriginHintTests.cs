using System.Security.Claims;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// The registered-origin hint (decision 2026-11-origin-hint, opt in with <c>Tenancy:OriginHint</c>): when several sites share one API address, the browser's Origin (or, for a plain
/// read such as an image, the Referer's site) names the tenant, but only when it is exactly a host some tenant answers to. Threats addressed: a forged or unregistered origin
/// choosing a tenant, the hint reaching setup or first-run, a token used for another tenant than the one chosen, shared caches mixing tenants, and the hint growing memory
/// with attacker-chosen names. Nothing changes while the setting is Off.
/// </summary>
public sealed class TenancyOriginHintTests
{
    private const string Api = "api.example.test";   // the shared API address: it is a registered host of tenant "scratch"
    private const string Scratch = "scratch.example.test";
    private const string Real = "real.example.test";

    private sealed class Resolver : ITenantResolver
    {
        public List<string> Asked { get; } = [];

        /// <summary>Hosts that resolve through a mirror pointer instead of directly.</summary>
        public HashSet<string> Mirrored { get; } = [];

        public Task<CurrentTenant> ResolveAsync(string? host, string? fallbackDomain = null, CancellationToken cancellationToken = default)
        {
            var name = (host ?? string.Empty).Split(':')[0].ToLowerInvariant();
            Asked.Add(name);
            (string Id, string Domain)? hit = name switch
            {
                Api or Scratch => ("scratch-id", Scratch),
                Real or "www." + Real => ("real-id", Real),
                _ => null,
            };
            if (hit is null)
            {
                return Task.FromResult(new CurrentTenant(null, null, null, host ?? string.Empty, TenantMatch.None));
            }

            return Task.FromResult(Mirrored.Contains(name)
                ? new CurrentTenant("scratch-id", "scratch", Scratch, host ?? string.Empty, TenantMatch.Mirror, hit.Value.Id, hit.Value.Domain)
                : new CurrentTenant(hit.Value.Id, hit.Value.Id, hit.Value.Domain, host ?? string.Empty, TenantMatch.Domain));
        }
    }

    private sealed class Names(params string[] names) : ITenantHostSource
    {
        public Task<IReadOnlyList<string>> AllNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(names);
    }

    private sealed record Outcome(DefaultHttpContext Context, bool Reached, CurrentTenant? Seen, Resolver Resolver, MemoryCache Cache);

    private static async Task<Outcome> RunAsync(
        string host, string method = "GET", string path = "/api/public/site", string? origin = null, string? referer = null,
        TenancyOriginHint hint = TenancyOriginHint.Registered, Resolver? resolver = null, bool withNames = true, MemoryCache? cache = null)
    {
        var services = new ServiceCollection();
        if (withNames)
        {
            services.AddSingleton<ITenantHostSource>(new Names(Api, Scratch, Real));
        }

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Host = new HostString(host);
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        if (referer is not null)
        {
            context.Request.Headers.Referer = referer;
        }

        resolver ??= new Resolver();
        cache ??= new MemoryCache(new MemoryCacheOptions());
        var holder = new RequestTenant();
        var reached = false;
        CurrentTenant? seen = null;
        var middleware = new TenantMiddleware(
            _ =>
            {
                reached = true;
                seen = holder.Current;
                return Task.CompletedTask;
            },
            Options.Create(new TenancyOptions { Enforcement = TenancyEnforcement.Enforce, OriginHint = hint, CacheSeconds = 60 }),
            cache,
            NullLogger<TenantMiddleware>.Instance);
        await middleware.InvokeAsync(context, resolver, holder);
        await context.Response.StartAsync();
        return new Outcome(context, reached, seen, resolver, cache);
    }

    [Fact]
    public async Task WhileOff_TheOriginIsNeverRead_TheHostDecides()
    {
        var run = await RunAsync(Api, origin: "https://" + Real, hint: TenancyOriginHint.Off);

        Assert.Equal("scratch-id", run.Seen!.Id);
        Assert.Null(run.Seen.Via);
        Assert.False(run.Context.Response.Headers.ContainsKey("Vary"));
    }

    [Fact]
    public async Task ARegisteredOrigin_ChoosesItsTenant_ThroughTheSharedApiAddress()
    {
        var run = await RunAsync(Api, origin: "https://" + Real);

        Assert.Equal("real-id", run.Seen!.Id);
        Assert.Equal(Real, run.Seen.Domain);
        Assert.Equal(TenantMatch.Domain, run.Seen.Match);
        Assert.Equal(Api, run.Seen.Host);      // the address the request reached is kept
        Assert.Equal(Real, run.Seen.Via);      // and so is the site that chose
    }

    [Theory]
    [InlineData("https://real.example.test:8443")]
    [InlineData("https://REAL.example.test")]
    [InlineData("https://www.real.example.test")]
    [InlineData("http://real.example.test")]
    public async Task TheOriginIsMatchedByItsHostName_NotItsSchemePortOrCase(string origin)
    {
        var run = await RunAsync(Api, origin: origin);

        Assert.Equal("real-id", run.Seen!.Id);
    }

    [Fact]
    public async Task TheSameTenantsOwnOrigin_ChangesTheTenantNotAtAll_ButIsNotedAsThePageAddress()
    {
        var run = await RunAsync(Api, origin: "https://" + Scratch);

        Assert.Equal("scratch-id", run.Seen!.Id);
        Assert.Equal(Scratch, run.Seen.Via);   // so removing that host name from the tenant is refused like removing the API address
    }

    [Theory]
    [InlineData("https://evil.example.test")]
    [InlineData("https://real.example.test.evil.test")]
    [InlineData("https://evilreal.example.test")]
    [InlineData("https://sub.real.example.test")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://real.example.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://real.example.test, https://evil.example.test")]
    [InlineData("https://user:pass@real.example.test")]
    public async Task AnUnregisteredOrOddOrigin_IsIgnored_TheHostDecides(string origin)
    {
        var run = await RunAsync(Api, origin: origin);

        Assert.Equal("scratch-id", run.Seen!.Id);
        Assert.Null(run.Seen.Via);
    }

    [Fact]
    public async Task OnAHostNoTenantOwns_TheHintIsNotUsed_SoSetupAndFirstRunAreUntouched()
    {
        var run = await RunAsync("unknown.example.test", path: "/api/public/site", origin: "https://" + Real);

        Assert.False(run.Reached);   // refused as an unknown host, exactly as without the hint
        Assert.Equal(404, run.Context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/setup/status")]
    [InlineData("/api/recovery/start")]
    public async Task TheExemptPaths_NeverTakeTheHint(string path)
    {
        var run = await RunAsync(Api, method: "POST", path: path, origin: "https://" + Real);

        Assert.Equal("scratch-id", run.Seen!.Id);
        Assert.Null(run.Seen.Via);
    }

    [Fact]
    public async Task APlainReadMayUseTheRefererSite_ForAnImageThatCarriesNoOrigin()
    {
        var run = await RunAsync(Api, method: "GET", path: "/api/media/1/pic.png", referer: "https://" + Real + "/post/some-slug?x=1");

        Assert.Equal("real-id", run.Seen!.Id);
        Assert.Equal(Real, run.Seen.Via);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ARequestThatChangesSomething_NeverUsesTheReferer(string method)
    {
        var run = await RunAsync(Api, method: method, path: "/api/comments", referer: "https://" + Real + "/post/x");

        Assert.Equal("scratch-id", run.Seen!.Id);
    }

    [Fact]
    public async Task ARequestThatChangesSomething_UsesTheOrigin()
    {
        var run = await RunAsync(Api, method: "POST", path: "/api/auth/token", origin: "https://" + Real);

        Assert.Equal("real-id", run.Seen!.Id);
    }

    [Fact]
    public async Task WhenBothAreSent_TheOriginWins_AndAnUnregisteredOriginIsNotRescuedByTheReferer()
    {
        var both = await RunAsync(Api, origin: "https://" + Real, referer: "https://" + Scratch + "/x");
        Assert.Equal("real-id", both.Seen!.Id);

        var bad = await RunAsync(Api, origin: "https://evil.example.test", referer: "https://" + Real + "/x");
        Assert.Equal("scratch-id", bad.Seen!.Id);
    }

    [Fact]
    public async Task AMirroringSite_IsFollowedLikeAHostWould()
    {
        var resolver = new Resolver();
        resolver.Mirrored.Add(Real);

        var run = await RunAsync(Api, origin: "https://" + Real, resolver: resolver);

        Assert.Equal(TenantMatch.Mirror, run.Seen!.Match);
        Assert.Equal("scratch-id", run.Seen.Id);          // it shows the tenant it mirrors
        Assert.Equal("real-id", run.Seen.OwnId);          // and still says who is mirroring
    }

    [Fact]
    public async Task EveryResponse_SaysItVariesByOriginAndReferer_SoNoSharedCacheMixesTenants()
    {
        var run = await RunAsync(Api, origin: "https://" + Real);

        var vary = string.Join(",", run.Context.Response.Headers.Vary.ToArray());
        Assert.Contains("Origin", vary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Referer", vary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttackerChosenOrigins_AreNeverRememberedOrAskedAbout()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new Resolver();
        for (var i = 0; i < 50; i++)
        {
            await RunAsync(Api, origin: $"https://junk-{i}.example.test", resolver: resolver, cache: cache);
        }

        Assert.DoesNotContain(resolver.Asked, name => name.StartsWith("junk-", StringComparison.Ordinal));
        Assert.True(cache.Count <= 3, $"the cache holds {cache.Count} entries; it must not grow with unregistered origins");
    }

    [Fact]
    public async Task WithoutTheRegisteredHostList_TheHintIsOff_FailClosed()
    {
        var run = await RunAsync(Api, origin: "https://" + Real, withNames: false);

        Assert.Equal("scratch-id", run.Seen!.Id);
    }

    [Fact]
    public async Task ATokenForAnotherTenantThanTheOneChosen_IsStillRefused()
    {
        var run = await RunAsync(Api, origin: "https://" + Real);
        var holder = new RequestTenant();
        holder.Set(run.Seen!);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(WellKnownClaims.Tenant, Scratch)], "test"));
        var reached = false;
        var token = new TenantTokenMiddleware(_ => { reached = true; return Task.CompletedTask; }, Options.Create(new TenancyOptions { Enforcement = TenancyEnforcement.Enforce }));

        await token.InvokeAsync(context, holder);

        Assert.False(reached);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public void ThePersonsPageAddress_IsProtectedLikeTheRequestAddress_WhenRemovingAnAlias()
    {
        Assert.Throws<Adventures.Common.Exceptions.BusinessRuleException>(() => TenantAddressGuard.ThrowIfCurrent(Api, Real, Real));
        TenantAddressGuard.ThrowIfCurrent(Api, "other.example.test", Real);   // another name is fine
    }
}
