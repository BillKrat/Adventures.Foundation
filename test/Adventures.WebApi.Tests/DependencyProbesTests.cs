using System.Net;
using System.Net.Http.Json;
using Adventures.Data.NQuad;
using Adventures.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.WebApi.Tests;

public sealed class DependencyProbesTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://mcp.test/") };
    }

    private static DependencyProbes Build(Handler handler, INQuadStore? store = null) =>
        new(new JwtTokenService(Options.Create(new JwtTokenOptions
        {
            Issuer = "i",
            Audience = "a",
            SigningKey = "0123456789abcdef0123456789abcdef",
            AccessTokenLifetime = TimeSpan.FromMinutes(5),
        })), new Factory(handler), store);

    [Fact]
    public async Task Mcp_Success_ReturnsTheHelloMessage_AndSendsABearerToken()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = "hi" }) });

        var result = await Build(handler).ProbeMcpAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("ok", result.Detail);
        Assert.Equal("hi", result.Message);
        Assert.Null(result.Failure);
        Assert.Equal("Bearer", handler.Last!.Headers.Authorization?.Scheme);
        Assert.Equal("api/hello", handler.Last.RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task Mcp_ErrorStatus_IsACodeWithAFailureToRecord()
    {
        var result = await Build(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))).ProbeMcpAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("http_401", result.Detail);
        Assert.Contains("401", result.Failure!.Message);
    }

    [Fact]
    public async Task Mcp_Exception_NeverEscapes_AndTheCodeNamesOnlyTheType()
    {
        var result = await Build(new Handler(_ => throw new HttpRequestException("secret host detail"))).ProbeMcpAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("exception_HttpRequestException", result.Detail);
        Assert.DoesNotContain("secret", result.Detail);
        Assert.IsType<HttpRequestException>(result.Failure);
    }

    [Fact]
    public async Task Database_IsSkippedWithoutAStore_AndOkWithOne()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.True((await Build(handler).ProbeDatabaseAsync(CancellationToken.None)).Skipped);

        var result = await Build(handler, new InMemoryNQuadStore()).ProbeDatabaseAsync(CancellationToken.None);
        Assert.True(result.Ok);
        Assert.False(result.Skipped);
        Assert.Equal("ok", result.Detail);
    }
}
