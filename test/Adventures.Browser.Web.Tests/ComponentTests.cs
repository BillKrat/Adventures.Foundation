using Adventures.Browser.Client;
using Adventures.Browser.Web.Components;
using Adventures.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Adventures.Browser.Web.Tests;

public class ComponentTests
{
    private sealed class Api : IBrowserApi
    {
        public bool SignedIn { get; set; }

        public bool IsSignedIn(string domain) => SignedIn;

        public Task<SiteSignInResult> SignInAsync(string domain, string userName, string password, CancellationToken cancellationToken = default) => Task.FromResult(SiteSignInResult.SignedIn);

        public void SignOut(string domain) => SignedIn = false;

        public Task<SchemaBrowserContext> GetContextAsync(string domain, CancellationToken cancellationToken = default) => Task.FromResult(new SchemaBrowserContext("Site One", domain));

        public Task<SchemaCatalogList> GetSchemasAsync(string domain, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SchemaCatalogList([new("urn:role", "identity", "Role", 2), new("urn:user", "identity", "User", 5)], []));

        public Task<SchemaDetail?> GetSchemaAsync(string domain, string schemaId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SchemaDetail?>(new SchemaDetail(schemaId, "identity", "Role", "Name",
                [new("Name", "<b>Name</b>", "string", "urn:p:name", true, false, true, null), new("Owner", "Owner", "ref:urn:user", "urn:p:owner", false, false, true, "urn:user")],
                [new("People", "People", "urn:user", "urn:p:role", true)],
                [new("urn:user", "User", "RoleId")]));
    }

    private sealed class Store(params string[] domains) : ISiteListStore
    {
        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(domains);

        public Task SaveAsync(IReadOnlyList<string> list, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<string> RenderAsync<TComponent>(IServiceCollection services, Dictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        await using var provider = services.AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters));
            await output.QuiescenceTask;
            return output.ToHtmlString();
        });
    }

    private static IServiceCollection With(BrowserShellViewModel shell) => new ServiceCollection().AddSingleton(shell);

    [Fact]
    public async Task With_no_domain_the_page_invites_one()
    {
        var html = await RenderAsync<Shell>(With(new BrowserShellViewModel(new Api(), new Store())));

        Assert.Contains("Add a domain to begin", html);
        Assert.Contains("id=\"new-domain\"", html);
        Assert.DoesNotContain("type=\"password\"", html);
    }

    [Fact]
    public async Task A_configured_domain_asks_for_a_sign_in()
    {
        var html = await RenderAsync<Shell>(With(new BrowserShellViewModel(new Api(), new Store("one.example"))));

        Assert.Contains("Sign in to one.example", html);
        Assert.Contains("type=\"password\"", html);
    }

    [Fact]
    public async Task The_schema_screen_lists_the_schemas_and_shows_one_with_its_links_as_text()
    {
        var browser = new SchemaBrowserViewModel(new Api(), "one.example");
        await browser.LoadAsync();
        await browser.OpenAsync("urn:role");

        var html = await RenderAsync<SchemaBrowser>(new ServiceCollection(), new() { ["Model"] = browser });

        Assert.Contains("Site One", html);
        Assert.Contains("identity", html);
        Assert.Contains("urn:p:owner", html);
        Assert.Contains("User.RoleId", html);
        Assert.Contains(">User</button>", html);          // a link shows the schema's name, its address on hover
        Assert.Contains("title=\"ref:urn:user\">ref</td>", html);
        Assert.Contains("Child collections", html);
        Assert.Contains("&lt;b&gt;Name&lt;/b&gt;", html); // what a site says is shown, never run as markup
        Assert.DoesNotContain("<b>Name</b>", html);
    }

    private sealed class Storage : IJSRuntime
    {
        public string? Value { get; set; }

        public bool Blocked { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (Blocked)
            {
                throw new JSException("blocked");
            }

            if (identifier == "localStorage.setItem")
            {
                Value = (string?)args![1];
                return default;
            }

            return new((TValue)(object?)Value!);
        }
    }

    [Fact]
    public async Task The_domain_list_is_kept_in_the_browsers_storage_and_survives_nonsense_there()
    {
        var storage = new Storage();
        var store = new LocalStorageSiteListStore(storage);

        Assert.Empty(await store.LoadAsync());
        await store.SaveAsync(["one.example"]);
        Assert.Equal(["one.example"], await store.LoadAsync());
        storage.Value = "[ \"two.example\", \"bad name\", null ]";
        Assert.Equal(["two.example"], await store.LoadAsync());
        storage.Value = "{ not a list";
        Assert.Empty(await store.LoadAsync());
        storage.Blocked = true;
        Assert.Empty(await store.LoadAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(["one.example"]));
    }
}
