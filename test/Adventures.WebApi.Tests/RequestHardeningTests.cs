// Assessed 2026-10-24 17:51 CT (Claude): automated scan for secrets, injection and unsafe patterns, re-run after the fixes - security findings F-1 to F-3 fixed in this file or in code it uses, covered by tests. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Net;
using System.Threading.RateLimiting;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

public class RequestHardeningTests
{
    private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return new ServiceCollection().AddLogging().AddAuthorization().AddRequestHardening(configuration).BuildServiceProvider();
    }

    private static DefaultHttpContext ClientAt(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }

    [Fact]
    public void An_endpoint_with_no_policy_requires_a_signed_in_user()
    {
        using var provider = Build();
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void The_token_limit_defaults_to_ten_a_minute_per_client()
    {
        var context = ClientAt("203.0.113.5");
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RequestHardening.ClientPartition(c, RequestHardening.DefaultTokensPerMinute));

        var granted = Enumerable.Range(0, 12).Count(_ => limiter.AttemptAcquire(context).IsAcquired);

        Assert.Equal(10, granted);
    }

    [Fact]
    public void The_health_limit_defaults_to_thirty_a_minute_per_client()
    {
        var context = ClientAt("203.0.113.5");
        var perMinute = RequestHardening.ResolveLimit(new ConfigurationBuilder().Build(), RequestHardening.HealthPerMinuteKey, RequestHardening.DefaultHealthPerMinute);
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RequestHardening.ClientPartition(c, perMinute));

        var granted = Enumerable.Range(0, 40).Count(_ => limiter.AttemptAcquire(context).IsAcquired);

        Assert.Equal(30, granted);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("0", 30)]
    [InlineData("-3", 30)]
    [InlineData("abc", 30)]
    public void A_configured_health_limit_is_used_only_when_it_is_a_positive_number(string configured, int expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [RequestHardening.HealthPerMinuteKey] = configured }).Build();

        Assert.Equal(expected, RequestHardening.ResolveLimit(configuration, RequestHardening.HealthPerMinuteKey, RequestHardening.DefaultHealthPerMinute));
    }

    [Fact]
    public void A_second_client_has_its_own_allowance()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RequestHardening.ClientPartition(c, 2));
        var first = ClientAt("203.0.113.5");
        var second = ClientAt("203.0.113.6");

        Assert.True(limiter.AttemptAcquire(first).IsAcquired);
        Assert.True(limiter.AttemptAcquire(first).IsAcquired);
        Assert.False(limiter.AttemptAcquire(first).IsAcquired);
        Assert.True(limiter.AttemptAcquire(second).IsAcquired);
    }

    [Fact]
    public void Security_headers_are_set_and_hsts_only_over_https_outside_development()
    {
        var http = new DefaultHttpContext();
        RequestHardening.SetSecurityHeaders(http, isDevelopment: false);
        Assert.Equal("nosniff", http.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", http.Response.Headers["X-Frame-Options"]);
        Assert.Equal("no-referrer", http.Response.Headers["Referrer-Policy"]);
        Assert.Contains("frame-ancestors 'none'", http.Response.Headers["Content-Security-Policy"].ToString());
        Assert.False(http.Response.Headers.ContainsKey("Strict-Transport-Security"));

        var https = new DefaultHttpContext();
        https.Request.Scheme = "https";
        RequestHardening.SetSecurityHeaders(https, isDevelopment: false);
        Assert.True(https.Response.Headers.ContainsKey("Strict-Transport-Security"));

        var development = new DefaultHttpContext();
        development.Request.Scheme = "https";
        RequestHardening.SetSecurityHeaders(development, isDevelopment: true);
        Assert.False(development.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public void A_later_component_may_tighten_a_header()
    {
        var context = new DefaultHttpContext();
        RequestHardening.SetSecurityHeaders(context, isDevelopment: false);

        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";

        Assert.Equal("default-src 'none'; sandbox", context.Response.Headers["Content-Security-Policy"]);
    }
}
