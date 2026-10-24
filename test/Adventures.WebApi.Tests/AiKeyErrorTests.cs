// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Net;
using Adventures.Entities;
using Adventures.Common.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>An expired or failing AI key is never silent: plain-words reasons, the exception log, and a health warning before it expires.</summary>
public sealed class AiKeyErrorTests : IDisposable
{
    public AiKeyErrorTests() => AiTriage.ResetForTests();

    public void Dispose() => AiTriage.ResetForTests();

    [Theory]
    [InlineData(401, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", "authentication_error", "key was rejected")]
    [InlineData(403, """{"error":{"type":"permission_error","message":"nope"}}""", "permission_error", "not allowed")]
    [InlineData(429, """{"error":{"type":"rate_limit_error","message":"slow down"}}""", "rate_limit_error", "rate limit")]
    [InlineData(400, """{"error":{"type":"invalid_request_error","message":"Your credit balance is too low"}}""", "invalid_request_error", "no credit left")]
    [InlineData(529, """{"error":{"type":"overloaded_error","message":"busy"}}""", "overloaded_error", "having trouble")]
    [InlineData(418, "not json", "unknown", "HTTP 418")]
    public void Explain_SaysWhatHappenedInPlainWords(int status, string body, string expectedKind, string expectedPhrase)
    {
        var (kind, reason) = AiTriage.Explain(status, body);

        Assert.Equal(expectedKind, kind);
        Assert.Contains(expectedPhrase, reason);
    }

    [Fact]
    public void Explain_ForARejectedKey_SaysHowToReplaceIt()
    {
        var (_, reason) = AiTriage.Explain(401, """{"error":{"type":"authentication_error","message":"invalid"}}""");

        Assert.Contains("Anthropic__ApiKey", reason);
        Assert.Contains("KeyExpiresOn", reason);
    }

    [Fact]
    public void Explain_NeverRepeatsASecretFromTheServicesMessage()
    {
        var (_, reason) = AiTriage.Explain(401, """{"error":{"type":"authentication_error","message":"invalid key sk-ant-api03-LEAKEDKEY123456 for bill@example.com"}}""");

        Assert.DoesNotContain("LEAKEDKEY", reason);
        Assert.DoesNotContain("bill@example.com", reason);
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.anthropic.test/") };
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task ARejectedKey_ReachesTheExceptionLog_AndTheButtonSaysWhy()
    {
        var log = new UnhandledExceptionHandlerTests.RecordingExceptionLog("e-1");
        var services = new ServiceCollection().AddSingleton<IExceptionLogBll>(log).BuildServiceProvider();
        var ai = new AiTriage(
            new Factory(new Handler(HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error","message":"invalid x-api-key"}}""")),
            Options.Create(new AnthropicOptions { ApiKey = "sk-ant-api03-TESTKEY12345678" }),
            services.GetRequiredService<IServiceScopeFactory>(),
            new FakeEnvironment(),
            NullLogger<AiTriage>.Instance);
        var report = new CorrelationReport("req-1", [], []);
        var rules = new TriageResult("req-1", "Nothing recorded", "", [], [], "cmd");

        var result = await ai.AskAsync(report, [], rules, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Contains("key was rejected", result.Reason);
        var recorded = Assert.Single(log.Throttled);
        Assert.Equal("ai triage", recorded.Request);
        Assert.Equal("req-1", recorded.CorrelationId);
        Assert.Contains("401", recorded.Exception.Message);
        Assert.DoesNotContain("TESTKEY", recorded.Exception.Message);
    }
}
