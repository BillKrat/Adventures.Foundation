using Adventures.Diagnostics.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Net;

using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.Diagnostics.WebApi.Tests;

/// <summary>A health warning before the AI key expires; the AI call's own failures are tested in Adventures.WebApi.Tests.</summary>
public sealed class AiKeyHealthTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<(HealthResponse Response, RecordingExceptionLog Log)> HealthOn(
        DateOnly today, DateOnly? expires, string? key = "sk-ant-api03-TESTKEY12345678")
    {
        var log = new RecordingExceptionLog("e-1");
        var controller = new HealthController(
            new DependencyProbes(
                new Adventures.Security.JwtTokenService(Options.Create(new Adventures.Security.JwtTokenOptions
                {
                    Issuer = "i",
                    Audience = "a",
                    SigningKey = "0123456789abcdef0123456789abcdef",
                    AccessTokenLifetime = TimeSpan.FromMinutes(5),
                })),
                new HealthFactory()),
            log,
            Options.Create(new AnthropicOptions { ApiKey = key, KeyExpiresOn = expires }),
            new FixedClock(new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));

        var result = await controller.Detail(CancellationToken.None);
        return (Assert.IsType<HealthResponse>(Assert.IsType<OkObjectResult>(result.Result).Value), log);
    }

    private sealed class HealthFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new Handler(HttpStatusCode.OK, """{"message":"hi"}"""), disposeHandler: true) { BaseAddress = new Uri("https://mcp.test/") };
    }

    [Fact]
    public async Task Health_WarnsFromFourteenDaysBeforeTheKeyExpires()
    {
        var (response, log) = await HealthOn(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 18));

        var key = Assert.Single(response.Dependencies!, d => d.Name == "ai-key");
        Assert.Equal("failed", key.Status);
        Assert.Equal("expires_in_14_days", key.Detail);
        Assert.Equal("Degraded", response.Status);
        var recorded = Assert.Single(log.Throttled, t => t.Request == "health probe: ai key");
        Assert.Equal(TimeSpan.FromHours(24), recorded.Window);
    }

    [Fact]
    public async Task Health_SaysExpiredAfterTheDate()
    {
        var (response, _) = await HealthOn(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 18));

        Assert.Equal("expired", Assert.Single(response.Dependencies!, d => d.Name == "ai-key").Detail);
    }

    [Fact]
    public async Task Health_IsQuiet_WhileTheKeyIsFarFromExpiry_OrWhenThereIsNoKeyOrDate()
    {
        var (far, farLog) = await HealthOn(new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 18));
        Assert.Equal("ok", Assert.Single(far.Dependencies!, d => d.Name == "ai-key").Status);
        Assert.Equal("Healthy", far.Status);
        Assert.DoesNotContain(farLog.Throttled, t => t.Request == "health probe: ai key");

        var (noDate, _) = await HealthOn(new DateOnly(2026, 11, 20), null);
        Assert.DoesNotContain(noDate.Dependencies!, d => d.Name == "ai-key");

        var (noKey, _) = await HealthOn(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 18), key: null);
        Assert.DoesNotContain(noKey.Dependencies!, d => d.Name == "ai-key");
    }
}
