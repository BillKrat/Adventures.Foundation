using System.Net;
using System.Text;
using Adventures.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Browser.Client.Tests;

public sealed class BrowserApiTests
{
    private sealed class Server : HttpMessageHandler
    {
        public List<(string Method, string Url, string? Authorization)> Calls { get; } = [];

        public Func<HttpRequestMessage, (HttpStatusCode, string)> Answer { get; set; } = request =>
            request.Method == HttpMethod.Post
                ? (HttpStatusCode.OK, $$"""{ "accessToken": "token-of-{{request.RequestUri!.Segments[^2].Trim('/')}}", "mustChangePassword": false }""")
                : (HttpStatusCode.OK, """{ "schemas": [], "unreadable": [] }""");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            var (status, json) = Answer(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static (BrowserApi Api, Server Server, HttpClient Http) Create()
    {
        var server = new Server();
        var http = new HttpClient(server) { BaseAddress = new Uri("https://browser.example/") };
        return (new BrowserApi(http), server, http);
    }

    [Fact]
    public async Task Each_site_has_its_own_token_which_never_travels_with_another()
    {
        var (api, server, http) = Create();

        Assert.Equal(SiteSignInResult.SignedIn, await api.SignInAsync("one.example", "ann", "pw"));
        Assert.Equal(SiteSignInResult.SignedIn, await api.SignInAsync("two.example", "ann", "pw"));
        await api.GetSchemasAsync("one.example");
        await api.GetSchemasAsync("TWO.example");
        await api.SignInAsync("one.example", "ann", "pw");

        Assert.Equal(
            [
                ("POST", "https://browser.example/api/sites/one.example/token", null),
                ("POST", "https://browser.example/api/sites/two.example/token", null),
                ("GET", "https://browser.example/api/sites/one.example/schemas", "Bearer token-of-one.example"),
                ("GET", "https://browser.example/api/sites/TWO.example/schemas", "Bearer token-of-two.example"),
                ("POST", "https://browser.example/api/sites/one.example/token", null),
            ],
            server.Calls);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task A_read_without_a_session_sends_nothing()
    {
        var (api, server, _) = Create();
        await api.SignInAsync("one.example", "ann", "pw");
        api.SignOut("one.example");
        server.Calls.Clear();

        Assert.True((await Assert.ThrowsAsync<ApiException>(() => api.GetSchemasAsync("one.example"))).IsUnauthorized);
        Assert.False(api.IsSignedIn("one.example"));
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task A_refused_sign_in_and_a_due_password_change_leave_no_session()
    {
        var (api, server, _) = Create();

        server.Answer = _ => (HttpStatusCode.Unauthorized, "{}");
        Assert.Equal(SiteSignInResult.Refused, await api.SignInAsync("one.example", "ann", "bad"));
        server.Answer = _ => (HttpStatusCode.OK, """{ "accessToken": "t", "mustChangePassword": true }""");
        Assert.Equal(SiteSignInResult.MustChangePassword, await api.SignInAsync("one.example", "ann", "pw"));

        Assert.False(api.IsSignedIn("one.example"));
    }

    [Fact]
    public async Task A_lapsed_session_is_forgotten()
    {
        var (api, server, _) = Create();
        await api.SignInAsync("one.example", "ann", "pw");
        server.Answer = _ => (HttpStatusCode.Unauthorized, """{ "title": "Sign-in was refused or has ended." }""");

        var refusal = await Assert.ThrowsAsync<ApiException>(() => api.GetSchemasAsync("one.example"));

        Assert.True(refusal.IsUnauthorized);
        Assert.False(api.IsSignedIn("one.example"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{ "title": "Not for this account." }""", "Not for this account.")]
    [InlineData(HttpStatusCode.BadGateway, "<html>secret detail</html>", "The server answered 502.")]
    [InlineData(HttpStatusCode.OK, "<html>secret detail</html>", "The server answered 200.")]
    [InlineData(HttpStatusCode.Redirect, "", "The server answered 302.")]
    public async Task A_failure_carries_one_safe_line(HttpStatusCode status, string body, string message)
    {
        var (api, server, _) = Create();
        await api.SignInAsync("one.example", "ann", "pw");
        server.Answer = _ => (status, body);

        Assert.Equal(message, (await Assert.ThrowsAsync<ApiException>(() => api.GetSchemasAsync("one.example"))).Message);
    }

    [Fact]
    public async Task A_schema_the_site_does_not_have_is_null_and_its_id_is_escaped()
    {
        var (api, server, _) = Create();
        await api.SignInAsync("one.example", "ann", "pw");
        server.Answer = _ => (HttpStatusCode.NotFound, "{}");

        Assert.Null(await api.GetSchemaAsync("one.example", "https://x.example/a#b&c"));
        Assert.EndsWith("schema?id=https%3A%2F%2Fx.example%2Fa%23b%26c", server.Calls[^1].Url);
        await Assert.ThrowsAsync<ApiException>(() => api.GetSchemasAsync("one.example"));
    }

    [Theory]
    [InlineData("https://browser.example")]
    [InlineData("http://localhost:5000/")]
    public void The_api_address_may_be_https_or_this_machine(string address)
    {
        using var provider = new ServiceCollection().AddAdventuresBrowserClient(new Uri(address)).BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IBrowserApi>());
    }

    [Fact]
    public void A_plain_http_address_elsewhere_is_refused() =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAdventuresBrowserClient(new Uri("http://browser.example/")));
}
