// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Net;
using System.Text.Json;
using Adventures.Common.Diagnostics;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

public sealed class AiTriageTests : IDisposable
{
    private const string SecretKey = "sk-ant-api03-THISISATESTKEY123456";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.anthropic.test/") };
    }

    public AiTriageTests() => AiTriage.ResetForTests();

    public void Dispose() => AiTriage.ResetForTests();

    private static HttpResponseMessage Answer(string json, int input = 900, int output = 250) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                content = new[] { new { type = "text", text = json } },
                usage = new { input_tokens = input, output_tokens = output },
            })),
        };

    private const string GoodJson =
        """Here you go: {"summary":"mcp had no certificate","likelyCause":"TLS binding missing","confidence":"high","nextSteps":["check SSL status","retry"],"missingEvidence":[]}""";

    private static AiTriage Build(FakeHandler handler, string? key = SecretKey, int maxPerHour = 20, string environmentName = "Production") =>
        new(
            new FakeFactory(handler),
            Options.Create(new AnthropicOptions { ApiKey = key, MaxCallsPerHour = maxPerHour }),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new FakeEnvironment(environmentName),
            NullLogger<AiTriage>.Instance);

    private sealed class FakeEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static (CorrelationReport Report, TriageResult Rules) Sample(string id = "req-1", string message = "The SSL connection could not be established")
    {
        var report = new CorrelationReport(
            id,
            [new ExceptionOccurrence("e1", DateTimeOffset.UtcNow, "System.Net.Http.HttpRequestException", message + " for bill@example.com", "health probe: mcp", "Admin", id, "s1")],
            [new ExceptionSignatureSummary("s1", "System.Net.Http.HttpRequestException", message, "Top.Frame", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 3, "New")]);
        var rules = new TriageResult(id, "Recovered", "A past mcp outage.", [new TriageFinding("Live check now", "mcp answers (18 ms)", "info")], ["Nothing to fix"], "cmd");
        return (report, rules);
    }

    [Fact]
    public async Task WithoutAKey_ItSaysNotSetUp_AndMakesNoCall()
    {
        var handler = new FakeHandler(_ => Answer(GoodJson));
        var (report, rules) = Sample();

        var result = await Build(handler, key: null).AskAsync(report, [], rules, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Contains("not set up", result.Reason);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task ASuccessfulCall_SendsTheKeyInTheHeaderOnly_AndParsesTheAnswer()
    {
        var handler = new FakeHandler(_ => Answer(GoodJson));
        var (report, rules) = Sample();

        var result = await Build(handler).AskAsync(report, [], rules, CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("mcp had no certificate", result.Summary);
        Assert.Equal("high", result.Confidence);
        Assert.Equal(["check SSL status", "retry"], result.NextSteps);
        Assert.Equal(900, result.InputTokens);
        Assert.Equal(250, result.OutputTokens);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("/v1/messages", call.Request.RequestUri!.AbsolutePath);
        Assert.Equal(SecretKey, call.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", call.Request.Headers.GetValues("anthropic-version").Single());
        Assert.DoesNotContain(SecretKey, call.Body);
    }

    [Fact]
    public async Task TheReportThatLeaves_IsRedacted()
    {
        var handler = new FakeHandler(_ => Answer(GoodJson));
        var (report, rules) = Sample();
        var lines = new[] { """{"@t":"2026-10-19T12:00:00Z","@l":"Error","@m":"call failed for 208.98.35.166 Password=hunter2","@x":"System.X: boom\n at A.B()"}""" };

        await Build(handler).AskAsync(report, lines, rules, CancellationToken.None);

        var body = Assert.Single(handler.Calls).Body;
        Assert.DoesNotContain("bill@example.com", body);
        Assert.DoesNotContain("208.98.35.166", body);
        Assert.DoesNotContain("hunter2", body);
        Assert.Contains("[email]", body);
        Assert.Contains("health probe: mcp", body);
        Assert.Contains("a signed-in user was involved: yes", body);
        Assert.DoesNotContain("\"Admin\"", body);
    }

    [Theory]
    [InlineData("Production", "Environment: Production")]
    [InlineData("Development", "a developer machine")]
    public async Task TheReportSaysWhichEnvironmentTheLiveChecksRanIn(string environmentName, string expected)
    {
        var handler = new FakeHandler(_ => Answer(GoodJson));
        var (report, rules) = Sample();

        await Build(handler, environmentName: environmentName).AskAsync(report, [], rules, CancellationToken.None);

        Assert.Contains(expected, Assert.Single(handler.Calls).Body);
    }

    [Fact]
    public async Task AnAnswer_IsCachedPerRequest_SoASecondClickCostsNothing()
    {
        var handler = new FakeHandler(_ => Answer(GoodJson));
        var ai = Build(handler);
        var (report, rules) = Sample();

        await ai.AskAsync(report, [], rules, CancellationToken.None);
        var second = await ai.AskAsync(report, [], rules, CancellationToken.None);

        Assert.True(second.FromCache);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task TheHourlyCap_StopsFurtherCalls()
    {
        var handler = new FakeHandler(_ => Answer(GoodJson));
        var ai = Build(handler, maxPerHour: 2);

        await ai.AskAsync(Sample("a").Report, [], Sample("a").Rules, CancellationToken.None);
        await ai.AskAsync(Sample("b").Report, [], Sample("b").Rules, CancellationToken.None);
        var third = await ai.AskAsync(Sample("c").Report, [], Sample("c").Rules, CancellationToken.None);

        Assert.False(third.Available);
        Assert.Contains("hourly limit", third.Reason);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task AnHttpError_ComesBackAsUnavailable_WithoutLeakingTheBody()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("rate limited secret detail") });

        var result = await Build(handler).AskAsync(Sample().Report, [], Sample().Rules, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Contains("rate limit", result.Reason);
        Assert.DoesNotContain("secret detail", result.Reason);
    }

    [Fact]
    public async Task ANonJsonAnswer_IsReportedAsUnexpected_AndNotCached()
    {
        var handler = new FakeHandler(_ => Answer("I cannot help with that."));
        var ai = Build(handler);

        var first = await ai.AskAsync(Sample().Report, [], Sample().Rules, CancellationToken.None);
        await ai.AskAsync(Sample().Report, [], Sample().Rules, CancellationToken.None);

        Assert.False(first.Available);
        Assert.Contains("not in the expected form", first.Reason);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task ANetworkFailure_IsUnavailable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("down"));

        var result = await Build(handler).AskAsync(Sample().Report, [], Sample().Rules, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Contains("could not be reached", result.Reason);
    }
}
