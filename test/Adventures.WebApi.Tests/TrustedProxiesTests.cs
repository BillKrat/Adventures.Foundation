using System.Net;
using Adventures.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// Running behind a CDN or reverse proxy (2026-11-23). The forwarded client address and scheme are honoured ONLY when the immediate caller is a configured proxy; a header from anyone else is
/// ignored, because trusting it would let any caller choose their own address (and so dodge every per-address limit) or fake https. The forwarded host is never honoured: the tenant stays
/// decided from the host the server saw. Off by default.
/// </summary>
public sealed class TrustedProxiesTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    /// <summary>Runs the real forwarded-headers middleware, built from the same options the host uses, and returns the context as the next component sees it.</summary>
    private static async Task<HttpContext> RunAsync(IConfiguration configuration, string caller, Action<HttpContext> request)
    {
        var options = new ForwardedHeadersOptions();
        TrustedProxies.Configure(options, configuration);
        HttpContext? seen = null;
        var middleware = new ForwardedHeadersMiddleware(context => { seen = context; return Task.CompletedTask; }, NullLoggerFactory.Instance, Options.Create(options));

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(caller);
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("origin.example.org");
        request(http);
        await middleware.Invoke(http);
        return seen!;
    }

    private static void Forwarded(HttpContext http, string forFor, string proto = "https", string host = "evil.example.net")
    {
        http.Request.Headers["X-Forwarded-For"] = forFor;
        http.Request.Headers["X-Forwarded-Proto"] = proto;
        http.Request.Headers["X-Forwarded-Host"] = host;
    }

    [Fact]
    public void ItIsOffUntilAProxyIsListed()
    {
        Assert.False(TrustedProxies.IsEnabled(Config()));
        Assert.False(TrustedProxies.IsEnabled(Config(("Proxy:Trusted", "  "))));
        Assert.True(TrustedProxies.IsEnabled(Config(("Proxy:Trusted", "10.0.0.5"))));
    }

    [Fact]
    public async Task AListedProxyMayTellUsTheVisitorAndTheScheme()
    {
        var context = await RunAsync(Config(("Proxy:Trusted", "10.0.0.5")), "10.0.0.5", http => Forwarded(http, "203.0.113.7"));

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task AHeaderFromAnyoneElseIsIgnored_SoNobodyCanChooseTheirOwnAddress()
    {
        var context = await RunAsync(Config(("Proxy:Trusted", "10.0.0.5")), "198.51.100.9", http => Forwarded(http, "203.0.113.7"));

        Assert.Equal(IPAddress.Parse("198.51.100.9"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task WithNoProxyListedNothingIsForwardedEvenFromLoopback()
    {
        var context = await RunAsync(Config(), "127.0.0.1", http => Forwarded(http, "203.0.113.7"));

        Assert.Equal(IPAddress.Parse("127.0.0.1"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task TheForwardedHostIsNeverUsed_SoTheTenantStaysDecidedFromTheHostTheServerSaw()
    {
        var context = await RunAsync(Config(("Proxy:Trusted", "10.0.0.5")), "10.0.0.5", http => Forwarded(http, "203.0.113.7"));

        Assert.Equal("origin.example.org", context.Request.Host.Value);
    }

    [Fact]
    public async Task ARangeOfAddressesCanBeListed_ForACdnThatPublishesOne()
    {
        var configuration = Config(("Proxy:Trusted:0", "198.51.100.0/24"), ("Proxy:Trusted:1", "2001:db8::/32"));

        var inside = await RunAsync(configuration, "198.51.100.77", http => Forwarded(http, "203.0.113.7"));
        var outside = await RunAsync(configuration, "198.51.101.1", http => Forwarded(http, "203.0.113.7"));

        Assert.Equal(IPAddress.Parse("203.0.113.7"), inside.Connection.RemoteIpAddress);
        Assert.Equal(IPAddress.Parse("198.51.101.1"), outside.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task AListWrittenAsOneCommaSeparatedValueWorks_AsAnEnvironmentVariableGivesIt()
    {
        var context = await RunAsync(Config(("Proxy:Trusted", "10.0.0.4, 10.0.0.5 ,10.0.0.6")), "10.0.0.5", http => Forwarded(http, "203.0.113.7"));

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task OnlyTheLastForwardedAddressCounts_SoAFakedFirstEntryChangesNothing()
    {
        // the visitor sent "X-Forwarded-For: 1.1.1.1"; the proxy appended the real address after it
        var context = await RunAsync(Config(("Proxy:Trusted", "10.0.0.5")), "10.0.0.5", http => Forwarded(http, "1.1.1.1, 203.0.113.7"));

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/99")]
    [InlineData("10.0.0.5, oops")]
    public void ABadEntryStopsTheStartWithTheNameOfTheSetting_NeverSilentlyTrustingNothingOrEverything(string value)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => TrustedProxies.Configure(new ForwardedHeadersOptions(), Config(("Proxy:Trusted", value))));

        Assert.Contains("Proxy:Trusted", failure.Message);
    }

    [Fact]
    public async Task TwoVisitorsBehindOneProxyGetTheirOwnRateLimitWindow()
    {
        var configuration = Config(("Proxy:Trusted", "10.0.0.5"));
        var first = await RunAsync(configuration, "10.0.0.5", http => Forwarded(http, "203.0.113.7"));
        var second = await RunAsync(configuration, "10.0.0.5", http => Forwarded(http, "203.0.113.8"));

        Assert.NotEqual(RequestHardening.ClientPartition(first, 5).PartitionKey, RequestHardening.ClientPartition(second, 5).PartitionKey);
    }

    [Fact]
    public void ALoopbackRequestThatCarriesTheProxyTraceIsNotLocal()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        http.Request.Headers["X-Original-For"] = "203.0.113.7:5000";

        Assert.False(HttpRequestOrigin.IsLocalRequest(http));
    }

    [Fact]
    public async Task ARequestThatCameThroughAProxyIsNeverLocal_EvenWhenTheProxyIsOnThisMachine()
    {
        var context = await RunAsync(Config(("Proxy:Trusted", "127.0.0.1")), "127.0.0.1", http => Forwarded(http, "203.0.113.7"));

        Assert.False(HttpRequestOrigin.IsLocalRequest(context));
    }
}
