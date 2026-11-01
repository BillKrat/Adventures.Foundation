using System.Security.Claims;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// T1, the trusted tenant (decision 2026-10-tenant-data-scoping): the tenant comes only from the host the server itself saw, never from a header the caller
/// can set; a host that matches no tenant is refused (or, while rolling out, reported); a token signed in for one tenant is refused on another's domain.
/// </summary>
public class TenancyTests
{
    private const string Acme = "acme.test";
    private const string Other = "other.test";

    /// <summary>Resolves the way the real one does (the domain, or a name under it) and records every host it was asked about.</summary>
    private sealed class FakeResolver(params string[] domains) : ITenantResolver
    {
        public List<string?> Asked { get; } = [];

        public Task<CurrentTenant> ResolveAsync(string? host, string? fallbackDomain = null, CancellationToken cancellationToken = default)
        {
            Asked.Add(host);
            var name = (host ?? string.Empty).Split(':')[0].ToLowerInvariant();
            var hit = domains.FirstOrDefault(d => name == d || name.EndsWith("." + d, StringComparison.Ordinal));
            if (hit is not null)
            {
                return Task.FromResult(new CurrentTenant(hit + "-id", hit, hit, host ?? string.Empty, TenantMatch.Domain));
            }

            if (!string.IsNullOrWhiteSpace(fallbackDomain) && domains.Contains(fallbackDomain))
            {
                return Task.FromResult(new CurrentTenant(fallbackDomain + "-id", fallbackDomain, fallbackDomain, host ?? string.Empty, TenantMatch.Fallback));
            }

            return Task.FromResult(new CurrentTenant(null, null, null, host ?? string.Empty, TenantMatch.None));
        }
    }

    private sealed class CapturingLogger : ILogger<TenantMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static TenancyOptions Options(TenancyEnforcement mode, string? fallback = null, bool allowFallback = false, int cacheSeconds = 60) =>
        new() { Enforcement = mode, FallbackDomain = fallback, AllowFallback = allowFallback, CacheSeconds = cacheSeconds };

    private static DefaultHttpContext Request(string host, string path = "/api/posts")
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<(DefaultHttpContext Context, bool Reached, CurrentTenant? Seen, FakeResolver Resolver, CapturingLogger Log)> RunAsync(
        DefaultHttpContext context, TenancyOptions options, FakeResolver? resolver = null, IMemoryCache? cache = null, CapturingLogger? log = null)
    {
        resolver ??= new FakeResolver(Acme);
        log ??= new CapturingLogger();
        var holder = new RequestTenant();
        var reached = false;
        CurrentTenant? seen = null;
        var middleware = new TenantMiddleware(
            ctx =>
            {
                reached = true;
                seen = holder.Current;
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Options.Options.Create(options),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            log);

        await middleware.InvokeAsync(context, resolver, holder);
        return (context, reached, seen, resolver, log);
    }

    // ---- the tenant comes from the host the server saw, and nothing else ----

    [Fact]
    public async Task TheTenantIsReadFromTheServerSeenHost_NeverFromOriginRefererOrForwardedHeaders()
    {
        var context = Request("api." + Acme);
        context.Request.Headers.Origin = "https://" + Other;
        context.Request.Headers.Referer = "https://" + Other + "/page";
        context.Request.Headers["X-Forwarded-Host"] = Other;
        context.Request.Headers["Forwarded"] = "host=" + Other;

        var result = await RunAsync(context, Options(TenancyEnforcement.Enforce), new FakeResolver(Acme, Other));

        Assert.True(result.Reached);
        Assert.Equal(Acme, result.Seen!.Domain);
        Assert.Equal(["api." + Acme], result.Resolver.Asked);
    }

    [Fact]
    public async Task SpoofedHeadersCannotRescueAnUnknownHost()
    {
        var context = Request("unbound.example");
        context.Request.Headers["X-Forwarded-Host"] = Acme;
        context.Request.Headers.Origin = "https://" + Acme;

        var result = await RunAsync(context, Options(TenancyEnforcement.Enforce), new FakeResolver(Acme));

        Assert.False(result.Reached);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    // ---- a host that matches no tenant ----

    [Fact]
    public async Task EnforceRefusesAHostThatMatchesNoTenant_With404AndNothingBehindIt()
    {
        var result = await RunAsync(Request("unbound.example"), Options(TenancyEnforcement.Enforce));

        Assert.False(result.Reached);
        Assert.Equal(StatusCodes.Status404NotFound, result.Context.Response.StatusCode);
    }

    [Fact]
    public async Task ReportServesAnUnknownHostButSaysSoOncePerHost_OffServesItSilently()
    {
        var log = new CapturingLogger();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var report = await RunAsync(Request("unbound.example"), Options(TenancyEnforcement.Report), log: log, cache: cache);
        Assert.True(report.Reached);
        Assert.Equal(TenantMatch.None, report.Seen!.Match);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("unbound.example"));

        // the same host again within the cache window: served, and not reported a second time
        await RunAsync(Request("unbound.example"), Options(TenancyEnforcement.Report), log: log, cache: cache);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);

        var off = new CapturingLogger();
        var silent = await RunAsync(Request("elsewhere.example"), Options(TenancyEnforcement.Off), log: off);
        Assert.True(silent.Reached);
        Assert.Empty(off.Entries);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/health")]
    public async Task TheInfrastructurePathsAreNeverRefused_SoACertificateRequestAndTheDeployCheckStillWork(string path)
    {
        var result = await RunAsync(Request("unbound.example", path), Options(TenancyEnforcement.Enforce));

        Assert.True(result.Reached);
    }

    [Fact]
    public async Task ASimilarPathIsNotExempt()
    {
        var result = await RunAsync(Request("unbound.example", "/api/health/detail"), Options(TenancyEnforcement.Enforce));

        Assert.False(result.Reached);
    }

    // ---- the development fallback ----

    [Fact]
    public async Task TheFallbackDomainWorksOnlyWhenTheHostAllowsIt_ThatIsInDevelopment()
    {
        var resolver = new FakeResolver(Acme);

        var refused = await RunAsync(Request("localhost:7052"), Options(TenancyEnforcement.Enforce, fallback: Acme, allowFallback: false), resolver);
        Assert.False(refused.Reached);

        var allowed = await RunAsync(Request("localhost:7052"), Options(TenancyEnforcement.Enforce, fallback: Acme, allowFallback: true), new FakeResolver(Acme));
        Assert.True(allowed.Reached);
        Assert.Equal(TenantMatch.Fallback, allowed.Seen!.Match);
    }

    // ---- not a store query on every request ----

    [Fact]
    public async Task TheAnswerIsKeptBrieflyPerHost_SoNotEveryRequestReadsTheTenantStore()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new FakeResolver(Acme, Other);

        await RunAsync(Request("api." + Acme), Options(TenancyEnforcement.Enforce), resolver, cache);
        await RunAsync(Request("api." + Acme), Options(TenancyEnforcement.Enforce), resolver, cache);
        await RunAsync(Request("api." + Other), Options(TenancyEnforcement.Enforce), resolver, cache);

        Assert.Equal(["api." + Acme, "api." + Other], resolver.Asked);
    }

    [Fact]
    public async Task ACacheTimeOfZeroAsksEveryTime()
    {
        var resolver = new FakeResolver(Acme);
        var cache = new MemoryCache(new MemoryCacheOptions());

        await RunAsync(Request("api." + Acme), Options(TenancyEnforcement.Enforce, cacheSeconds: 0), resolver, cache);
        await RunAsync(Request("api." + Acme), Options(TenancyEnforcement.Enforce, cacheSeconds: 0), resolver, cache);

        Assert.Equal(2, resolver.Asked.Count);
    }

    // ---- a token signed in for one tenant, replayed on another's domain ----

    private static async Task<(bool Reached, int Status)> TokenCheckAsync(string host, ClaimsPrincipal user, TenancyEnforcement mode = TenancyEnforcement.Enforce)
    {
        var context = Request(host);
        context.User = user;
        var holder = new RequestTenant();
        holder.Set(await new FakeResolver(Acme, Other).ResolveAsync(host));
        var reached = false;
        var middleware = new TenantTokenMiddleware(ctx => { reached = true; return Task.CompletedTask; }, Microsoft.Extensions.Options.Options.Create(Options(mode)));

        await middleware.InvokeAsync(context, holder);
        return (reached, context.Response.StatusCode);
    }

    private static ClaimsPrincipal Person(string? tenantClaim)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "ann") };
        if (tenantClaim is not null)
        {
            claims.Add(new Claim(WellKnownClaims.Tenant, tenantClaim));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    [Fact]
    public async Task ATokenSignedInForTheSameTenantPasses_EvenInADifferentCase()
    {
        Assert.True((await TokenCheckAsync("api." + Acme, Person("ACME.test"))).Reached);
    }

    [Fact]
    public async Task ATokenSignedInForAnotherTenantIsRefusedWith403()
    {
        var (reached, status) = await TokenCheckAsync("api." + Other, Person(Acme));

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task AMachineTokenWithNoTenantClaim_AnAnonymousCaller_AndTenancyOff_AreNotChecked()
    {
        Assert.True((await TokenCheckAsync("api." + Other, Person(null))).Reached);
        Assert.True((await TokenCheckAsync("api." + Other, new ClaimsPrincipal(new ClaimsIdentity()))).Reached);
        Assert.True((await TokenCheckAsync("api." + Other, Person(Acme), TenancyEnforcement.Off)).Reached);
    }

    // ---- what the rest of the request sees ----

    [Fact]
    public async Task TheResolvedTenantIsAvailableToTheRestOfTheRequest()
    {
        var result = await RunAsync(Request("www." + Other), Options(TenancyEnforcement.Enforce), new FakeResolver(Acme, Other));

        Assert.Equal(Other, result.Seen!.Domain);
        Assert.Equal(Other + "-id", result.Seen.Id);
    }
}
