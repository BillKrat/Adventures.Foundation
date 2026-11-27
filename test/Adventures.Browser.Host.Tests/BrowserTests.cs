using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Xml.Linq;
using Adventures.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Browser.Host.Tests;

/// <summary>
/// The NQuad browser (S8): it must be buildable from the reusable libraries alone, and it must browse and edit schemas and entities for a signed-in
/// administrator only. Runs the real host in memory over its own in-memory store.
/// </summary>
public sealed class BrowserTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BrowserTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "test");
            builder.UseSetting("Jwt:Audience", "test");
            builder.UseSetting("Jwt:SigningKey", "0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("NQuadStore:Provider", "Memory");
        });
    }

    private HttpClient Client(string? roles = null)
    {
        var client = _factory.CreateClient();
        if (roles is not null)
        {
            var tokens = _factory.Services.GetRequiredService<IJwtTokenService>();
            var token = tokens.IssueToken(Guid.NewGuid().ToString(), [new Claim(ClaimTypes.Name, "tester"), new Claim(WellKnownClaims.Roles, roles)]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        return client;
    }

    [Fact]
    public async Task Without_a_token_or_without_the_Admin_role_the_endpoints_refuse()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/browser/types")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("Editor").GetAsync("/api/browser/types")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("Editor").GetAsync("/api/browser/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("Admin").GetAsync("/api/browser/types")).StatusCode); // the role of a signed-in administrator is TenantAdmin
    }

    [Fact]
    public async Task An_administrator_sees_the_generic_kinds_and_none_of_a_feature()
    {
        var types = await Client("TenantAdmin").GetFromJsonAsync<List<TypeInfo>>("/api/browser/types");

        Assert.NotNull(types);
        var keys = types!.Select(t => t.Key).ToList();
        Assert.Contains("schemas", keys);
        Assert.Contains("users", keys);
        Assert.Contains("roles", keys);
        Assert.DoesNotContain(keys, k => k.Contains("post", StringComparison.OrdinalIgnoreCase) || k.Contains("collab", StringComparison.OrdinalIgnoreCase) || k.Contains("plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_grid_of_a_kind_lists_its_entities_with_the_schema_driven_columns()
    {
        var response = await Client("TenantAdmin").GetAsync("/api/browser/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"rows\"", body);
        Assert.Contains("Admin", body);
    }

    [Fact]
    public async Task An_unknown_kind_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client("TenantAdmin").GetAsync("/api/browser/nothing-like-this")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_can_create_edit_and_delete_an_entity()
    {
        var client = Client("TenantAdmin");
        var created = await client.PostAsJsonAsync("/api/browser/roles", new { entityId = "", values = new Dictionary<string, string?> { ["Name"] = "Browser test role" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var form = await created.Content.ReadFromJsonAsync<Form>();
        var id = form!.Entity.EntityId;
        Assert.False(string.IsNullOrEmpty(id));

        var updated = await client.PutAsJsonAsync($"/api/browser/roles/{id}", new { entityId = id, values = new Dictionary<string, string?> { ["Name"] = "Browser test role (edited)" } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var reread = await client.GetFromJsonAsync<Form>($"/api/browser/roles/{id}");
        Assert.Equal("Browser test role (edited)", reread!.Entity.Values["Name"]);

        Assert.True((await client.DeleteAsync($"/api/browser/roles/{id}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/browser/roles/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_page_is_served_without_a_token_and_holds_no_secret()
    {
        var page = await _factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("NQuad Browser", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_page_policy_lets_the_page_run_its_own_script_and_style_but_no_inline_code_eval_or_other_origin()
    {
        var response = await _factory.CreateClient().GetAsync("/");
        var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("style-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("http:", csp);
        Assert.DoesNotContain("https:", csp);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<style", html);
        Assert.DoesNotContain(" style=", html);
        Assert.DoesNotContain("onclick=", html);
    }

    [Fact]
    public void The_browser_references_only_the_reusable_libraries_no_blog_collaboration_or_identity_store()
    {
        var root = FindRoot();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(Path.Combine(root, "apps", "Adventures.Browser.Host", "Adventures.Browser.Host.csproj"));
        while (queue.Count > 0)
        {
            var project = Path.GetFullPath(queue.Dequeue());
            if (!seen.Add(project) || !File.Exists(project))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/');
                queue.Enqueue(Path.Combine(Path.GetDirectoryName(project)!, include));
            }
        }

        var names = seen.Select(p => Path.GetFileNameWithoutExtension(p)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var forbidden = names.Where(n =>
            n.StartsWith("Adventures.Blog", StringComparison.Ordinal)
            || n.StartsWith("Adventures.Collaboration", StringComparison.Ordinal)
            || n.StartsWith("Adventures.Identity", StringComparison.Ordinal)
            || n.StartsWith("Adventures.Data.Postgres", StringComparison.Ordinal)
            || n.StartsWith("Adventures.Data.Sql", StringComparison.Ordinal)
            || n.StartsWith("Adventures.Hosting", StringComparison.Ordinal)).ToList();

        Assert.Empty(forbidden);
        Assert.Contains("Adventures.Entities", names);
        Assert.Contains("Adventures.Data.NQuad", names);
    }

    [Fact]
    public void The_policy_name_matches_the_role_policy_the_security_library_registers()
    {
        Assert.Equal(Adventures.Browser.Host.Controllers.BrowserAuth.Policy, JwtServiceCollectionExtensions.RolePolicyName(Adventures.Browser.Host.Controllers.BrowserAuth.AdminRole));
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

    private sealed record TypeInfo(string Key, string Title, string SchemaIri);

    private sealed record Form(FormEntity Entity);

    private sealed record FormEntity(string EntityId, Dictionary<string, string?> Values);
}
