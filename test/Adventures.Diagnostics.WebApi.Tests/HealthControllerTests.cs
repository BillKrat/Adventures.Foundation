using Adventures.Diagnostics.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Net;
using System.Net.Http.Json;
using Adventures.Common.Diagnostics;
using Adventures.Security;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.Diagnostics.WebApi.Tests;

public class HealthControllerTests
{
    private static IJwtTokenService BuildTokenService() =>
        new JwtTokenService(Options.Create(new JwtTokenOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "0123456789abcdef0123456789abcdef",
            AccessTokenLifetime = TimeSpan.FromMinutes(30),
        }));

    /// <summary>Hand-rolled test double, not a mocking library - matches this repo's established convention.</summary>
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://mcp.example.test/") };
    }

    private static IDependencyProbes Probes(FakeHttpMessageHandler handler, Adventures.Data.NQuad.INQuadStore? store = null) =>
        new Adventures.WebApi.DependencyProbes(BuildTokenService(), new FakeHttpClientFactory(handler), store is null ? null : new Adventures.Data.NQuad.NQuadStoreProbe(store));

    private static HealthController BuildController(FakeHttpMessageHandler handler) =>
        new(Probes(handler));

    [Fact]
    public async Task Detail_IncludesMcpMessage_WhenMcpServerRespondsSuccessfully()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { message = "hello world" }),
        });
        var controller = BuildController(handler);

        var result = await controller.Detail(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.Equal("Healthy", response.Status);
        Assert.Equal("ok", Assert.Single(response.Dependencies!, d => d.Name == "mcp").Status);
        Assert.Equal("hello world", response.McpMessage);
        Assert.Equal("ok", response.McpStatus);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequest.Headers.Authorization?.Parameter));
        Assert.Equal("api/hello", handler.LastRequest.RequestUri?.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task Detail_ReturnsHealthyWithNullMcpMessage_WhenMcpServerIsUnreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var controller = BuildController(handler);

        var result = await controller.Detail(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.Equal("Degraded", response.Status);
        Assert.Null(response.McpMessage);
        Assert.StartsWith("exception_", response.McpStatus);
        var mcp = Assert.Single(response.Dependencies!, d => d.Name == "mcp");
        Assert.Equal("failed", mcp.Status);
        Assert.StartsWith("exception_", mcp.Detail);
    }

    [Fact]
    public async Task Detail_ReturnsHealthyWithNullMcpMessage_WhenMcpServerReturnsErrorStatus()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var controller = BuildController(handler);

        var result = await controller.Detail(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.Null(response.McpMessage);
        Assert.Equal("http_401", response.McpStatus);
        Assert.Equal("Degraded", response.Status);
    }

    private sealed class CountingStore(Func<Task<long>> count) : Adventures.Data.NQuad.INQuadStore
    {
        public Task<long> CountAsync(CancellationToken cancellationToken = default) => count();

        public Task<int> InsertAsync(Adventures.Data.NQuad.NQuad quad, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> InsertManyAsync(IReadOnlyCollection<Adventures.Data.NQuad.NQuad> quads, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Adventures.Data.NQuad.NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Adventures.Data.NQuad.NQuad>> QueryBySubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static HealthController BuildController(FakeHttpMessageHandler handler, Adventures.Data.NQuad.INQuadStore store) =>
        new(Probes(handler, store));

    private static FakeHttpMessageHandler HealthyMcp() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = "hi" }) });

    [Fact]
    public async Task Detail_IsUnhealthyWith503_WhenTheDatabaseFails_EvenIfMcpIsFine()
    {
        var controller = BuildController(HealthyMcp(), new CountingStore(() => throw new InvalidOperationException("db down")));

        var result = await controller.Detail(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, objectResult.StatusCode);
        var response = Assert.IsType<HealthResponse>(objectResult.Value);
        Assert.Equal("Unhealthy", response.Status);
        Assert.Equal("failed", Assert.Single(response.Dependencies!, d => d.Name == "database").Status);
    }

    [Fact]
    public async Task Detail_IsHealthy_WhenMcpAndDatabaseAreFine()
    {
        var controller = BuildController(HealthyMcp(), new CountingStore(() => Task.FromResult(42L)));

        var result = await controller.Detail(CancellationToken.None);

        var response = Assert.IsType<HealthResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Healthy", response.Status);
        Assert.All(response.Dependencies!, d => Assert.Equal("ok", d.Status));
        Assert.Equal(2, response.Dependencies!.Count);
    }

    // Backlog 3a: the anonymous answer says only whether the API is up; the dependency detail needs a right.

    [Fact]
    public async Task Get_AnswersOnlyStatusAndTime_NeverDependenciesOrMcpText()
    {
        var controller = BuildController(HealthyMcp(), new CountingStore(() => Task.FromResult(42L)));

        var result = await controller.Get(CancellationToken.None);

        var response = Assert.IsType<PublicHealthResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Healthy", response.Status);
        var json = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.DoesNotContain("ependenc", json);
        Assert.DoesNotContain("mcp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orrelation", json);
    }

    [Fact]
    public async Task Get_IsStill503WhenTheDatabaseFails_SoADeploySmokeTestFails_ButSaysNothingAboutWhy()
    {
        var controller = BuildController(HealthyMcp(), new CountingStore(() => throw new InvalidOperationException("db down")));

        var result = await controller.Get(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, objectResult.StatusCode);
        var response = Assert.IsType<PublicHealthResponse>(objectResult.Value);
        Assert.Equal("Unhealthy", response.Status);
    }

    [Fact]
    public void Get_IsAnonymousAndRateLimited_AndDetailNeedsTheDiagnosticsRight()
    {
        var get = typeof(HealthController).GetMethod(nameof(HealthController.Get))!;
        var detail = typeof(HealthController).GetMethod(nameof(HealthController.Detail))!;

        Assert.Equal(Adventures.WebApi.RequestHardening.HealthPolicy,
            Assert.Single(get.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true).Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()).PolicyName);
        Assert.Equal(Adventures.Entities.Rights.DiagnosticsRead,
            Assert.Single(detail.GetCustomAttributes(typeof(Adventures.WebApi.RequireRightAttribute), true).Cast<Adventures.WebApi.RequireRightAttribute>()).Right);
        Assert.Empty(detail.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
    }

    [Fact]
    public async Task GetAndDetail_ShareOneProbeRunWhileTheSnapshotIsFresh()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = "hi" }) };
        });
        var cache = new HealthSnapshotCache(TimeSpan.FromSeconds(15), TimeProvider.System);
        var controller = new HealthController(Probes(handler), snapshots: cache);

        await controller.Get(CancellationToken.None);
        await controller.Get(CancellationToken.None);
        await controller.Detail(CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Detail_RecordsAnUnreachableMcpInTheExceptionLog_ThrottledToOncePerHour()
    {
        var log = new RecordingExceptionLog("id-1");
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var controller = new HealthController(Probes(handler), log);

        await controller.Detail(CancellationToken.None);

        var recorded = Assert.Single(log.Throttled);
        Assert.Equal("health probe: mcp", recorded.Request);
        Assert.Equal(TimeSpan.FromHours(1), recorded.Window);
    }

    [Fact]
    public async Task Detail_RecordsAnMcpErrorStatus_ButNotASuccess()
    {
        var log = new RecordingExceptionLog("id-1");
        var failing = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await new HealthController(Probes(failing), log)
            .Detail(CancellationToken.None);
        Assert.Single(log.Throttled);

        var healthy = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = "hi" }) });
        await new HealthController(Probes(healthy), log)
            .Detail(CancellationToken.None);
        Assert.Single(log.Throttled);
    }
}
