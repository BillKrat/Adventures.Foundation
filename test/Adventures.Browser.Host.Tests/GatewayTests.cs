using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Adventures.Browser.Gateway.WebApi;
using Adventures.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Browser.Host.Tests;

/// <summary>The browser API as it runs: the real host, with the configured sites played by a stand-in that records what reaches it.</summary>
public sealed class GatewayTests
{
    private sealed class Sites : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Authorization, string Headers, string? Body)> Calls { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Json { get; set; } = "{}";

        public Exception? Failure { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), string.Join(',', request.Headers.Select(h => h.Key)),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (Failure is not null)
            {
                throw Failure;
            }

            var response = new HttpResponseMessage(Status) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };
            response.Headers.Location = new Uri("https://elsewhere.example/");
            return response;
        }
    }

    private const string TokenJson = """{ "accessToken": "token-1", "expiresAtUtc": "2030-01-01T00:00:00Z", "mustChangePassword": false, "internal": "do-not-pass" }""";

    private static (WebApplicationFactory<Program> Factory, Sites Sites) Host(params (string Key, string Value)[] settings)
    {
        var sites = new Sites();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BrowserGateway:Sites:0:Domain", "one.example");
            builder.UseSetting("BrowserGateway:Sites:0:Api", "https://api.one.example");
            builder.UseSetting("BrowserGateway:Sites:1:Domain", "two.example");
            builder.UseSetting("BrowserGateway:Sites:1:Api", "https://two.example/blog/");
            builder.UseSetting("BrowserGateway:AllowedOrigins:0", "https://browser.example");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services => services.AddHttpClient(SiteForwarder.ClientName).ConfigurePrimaryHttpMessageHandler(() => sites));
        });
        return (factory, sites);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? token = null, string address = "https://browser-api.example/")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(address) });
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_sign_in_goes_to_the_configured_site_and_only_the_token_comes_back()
    {
        var (factory, sites) = Host();
        using var _ = factory;
        sites.Json = TokenJson;

        var response = await Client(factory).PostAsJsonAsync("/api/sites/ONE.example/token", new SiteSignIn("ann", "pw"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("token-1", (await response.Content.ReadFromJsonAsync<SiteToken>())!.AccessToken);
        Assert.DoesNotContain("do-not-pass", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var call = Assert.Single(sites.Calls);
        Assert.Equal((HttpMethod.Post, "https://api.one.example/api/auth/token"), (call.Method, call.Url));
        Assert.Contains("\"ann\"", call.Body);
        Assert.Null(call.Authorization);
    }

    [Theory]
    [InlineData("/api/sites/two.example/context", "https://two.example/blog/api/browser/context", """{ "siteName": "Two", "siteDomain": "two.example" }""")]
    [InlineData("/api/sites/two.example/schemas", "https://two.example/blog/api/browser/schemas", """{ "schemas": [], "unreadable": [] }""")]
    [InlineData("/api/sites/two.example/schema?id=https%3A%2F%2Fx.example%2Fa%23b%26c", "https://two.example/blog/api/browser/schema?id=https%3A%2F%2Fx.example%2Fa%23b%26c", """{ "id": "x", "area": "a", "name": "b", "fields": [], "relations": [], "usedBy": [], "internal": "do-not-pass" }""")]
    public async Task A_read_carries_the_persons_token_and_nothing_else(string path, string forwarded, string answer)
    {
        var (factory, sites) = Host();
        using var _ = factory;
        sites.Json = answer;
        var client = Client(factory, "token-1");
        client.DefaultRequestHeaders.Add("Cookie", "a=b");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "evil.example");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("do-not-pass", await response.Content.ReadAsStringAsync());
        var call = Assert.Single(sites.Calls);
        Assert.Equal((HttpMethod.Get, forwarded, "Bearer token-1"), (call.Method, call.Url, call.Authorization));
        Assert.Equal("Authorization", call.Headers);
    }

    [Fact]
    public async Task Without_a_token_or_for_a_site_not_configured_nothing_is_sent()
    {
        var (factory, sites) = Host();
        using var _ = factory;
        sites.Json = TokenJson;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(factory).GetAsync("/api/sites/one.example/schemas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(factory, "t").GetAsync("/api/sites/other.example/schemas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(factory).PostAsJsonAsync("/api/sites/other.example/token", new SiteSignIn("ann", "pw"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(factory).PostAsJsonAsync("/api/sites/api.one.example/token", new SiteSignIn("ann", "pw"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(factory).PostAsJsonAsync("/api/sites/one.example/token", new SiteSignIn("ann", new string('x', 300)))).StatusCode);
        Assert.Empty(sites.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Redirect, HttpStatusCode.BadGateway)]
    public async Task A_refusal_is_passed_on_in_the_gateways_words(HttpStatusCode fromSite, HttpStatusCode expected)
    {
        var (factory, sites) = Host();
        using var _ = factory;
        sites.Status = fromSite;
        sites.Json = """{ "title": "stack trace at Secret.Place" }""";

        var read = await Client(factory, "t").GetAsync("/api/sites/one.example/schemas");
        var signIn = await Client(factory).PostAsJsonAsync("/api/sites/one.example/token", new SiteSignIn("ann", "pw"));

        Assert.Equal((expected, expected), (read.StatusCode, signIn.StatusCode));
        Assert.DoesNotContain("Secret.Place", await read.Content.ReadAsStringAsync() + await signIn.Content.ReadAsStringAsync());
        Assert.Equal(2, sites.Calls.Count); // a redirect is not followed
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("slow")]
    [InlineData("nonsense")]
    public async Task A_site_that_cannot_be_read_is_a_bad_gateway(string how)
    {
        var (factory, sites) = Host();
        using var _ = factory;
        sites.Failure = how == "unreachable" ? new HttpRequestException("no route") : how == "slow" ? new TaskCanceledException() : null;
        sites.Json = "<html>";

        Assert.Equal(HttpStatusCode.BadGateway, (await Client(factory, "t").GetAsync("/api/sites/one.example/schemas")).StatusCode);
    }

    [Fact]
    public void The_real_connection_follows_no_redirect_and_keeps_no_cookie()
    {
        using var handler = SiteForwarder.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task Sign_ins_are_limited_per_visitor()
    {
        var (factory, sites) = Host(("BrowserGateway:SignInsPerMinute", "2"));
        using var _ = factory;
        sites.Status = HttpStatusCode.Unauthorized;
        var client = Client(factory);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/sites/one.example/token", new SiteSignIn("ann", "pw"))).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(2, sites.Calls.Count);
    }

    [Theory]
    [InlineData("http://browser-api.example/", HttpStatusCode.BadRequest)]
    [InlineData("http://localhost/", HttpStatusCode.OK)]
    [InlineData("https://browser-api.example/", HttpStatusCode.OK)]
    public async Task A_password_travels_over_https_unless_it_stays_on_this_machine(string address, HttpStatusCode expected)
    {
        var (factory, sites) = Host();
        using var _ = factory;
        sites.Json = TokenJson;

        Assert.Equal(expected, (await Client(factory, address: address).PostAsJsonAsync("/api/sites/one.example/token", new SiteSignIn("ann", "pw"))).StatusCode);
    }

    [Theory]
    [InlineData("https://browser.example", true)]
    [InlineData("https://evil.example", false)]
    public async Task Only_a_listed_web_page_may_call_from_a_browser(string origin, bool allowed)
    {
        var (factory, _) = Host();
        using var __ = factory;
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/sites/one.example/schemas");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await Client(factory).SendAsync(preflight);

        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("/api/sites")]
    [InlineData("/api/sites/one.example")]
    [InlineData("/api/browser/types")]
    [InlineData("/")]
    public async Task No_route_lists_the_sites_and_the_old_browser_is_gone(string path)
    {
        var (factory, _) = Host();
        using var __ = factory;

        Assert.Equal(HttpStatusCode.NotFound, (await Client(factory, "t").GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Nothing_but_a_sign_in_can_be_posted_and_the_health_route_answers_with_the_security_headers()
    {
        var (factory, sites) = Host();
        using var _ = factory;
        var client = Client(factory, "t");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/sites/one.example/schemas", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("/api/sites/one.example/schema?id=a")).StatusCode);
        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("default-src 'none'", string.Join(' ', health.Headers.GetValues("Content-Security-Policy")));
        Assert.Empty(sites.Calls);
    }

    [Theory]
    [InlineData("http://api.remote.example", "one.example", "https")]
    [InlineData("https://user:pw@api.example", "one.example", "https")]
    [InlineData("https://api.example?x=1", "one.example", "https")]
    [InlineData("not an address", "one.example", "https")]
    [InlineData("https://api.example", "https://one.example", "host name")]
    [InlineData("https://api.example", "", "host name")]
    public void A_bad_site_entry_stops_the_start(string api, string domain, string why)
    {
        var options = new BrowserGatewayOptions { Sites = [new BrowserSiteOptions { Domain = domain, Api = api }] };

        var error = Assert.Throws<InvalidOperationException>(() => new SiteDirectory(options));

        Assert.Contains("BrowserGateway:Sites:0", error.Message);
        Assert.Contains(why, error.Message);
    }

    [Fact]
    public void A_domain_listed_twice_stops_the_start()
    {
        var site = new BrowserSiteOptions { Domain = "one.example", Api = "https://api.one.example" };

        Assert.Contains("twice", Assert.Throws<InvalidOperationException>(() => new SiteDirectory(new BrowserGatewayOptions { Sites = [site, site] })).Message);
    }

    [Fact]
    public void The_host_reaches_no_store_no_feature_and_no_user_store()
    {
        var root = FindRoot();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([Path.Combine(root, "apps", "Adventures.Browser.Host", "Adventures.Browser.Host.csproj")]);
        while (queue.TryDequeue(out var next))
        {
            var project = Path.GetFullPath(next);
            if (!seen.Add(project))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                queue.Enqueue(Path.Combine(Path.GetDirectoryName(project)!, ((string)reference.Attribute("Include")!).Replace('\\', '/')));
            }
        }

        var names = seen.Select(p => Path.GetFileNameWithoutExtension(p)!).ToList();

        Assert.DoesNotContain(names, n => n.StartsWith("Adventures.Data", StringComparison.Ordinal) || n.StartsWith("Adventures.Blog", StringComparison.Ordinal)
            || n.StartsWith("Adventures.Collaboration", StringComparison.Ordinal) || n == "Adventures.Hosting" || n == "Adventures.Identity.Postgres");
        Assert.DoesNotContain("UserSecretsId", File.ReadAllText(Path.Combine(root, "apps", "Adventures.Browser.Host", "Adventures.Browser.Host.csproj")));
    }

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "Adventures.Foundation.slnx")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("Adventures.Foundation.slnx not found.");
    }
}
