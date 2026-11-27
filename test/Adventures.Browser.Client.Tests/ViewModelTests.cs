using System.Net;
using Adventures.Client;
using Adventures.Entities;
using Xunit;

namespace Adventures.Browser.Client.Tests;

public sealed class ViewModelTests
{
    private sealed class Store(params string[] domains) : ISiteListStore
    {
        public IReadOnlyList<string> Saved { get; private set; } = domains;

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) => Failure is null ? Task.FromResult(Saved) : throw Failure;

        public Task SaveAsync(IReadOnlyList<string> list, CancellationToken cancellationToken = default)
        {
            Saved = list;
            return Task.CompletedTask;
        }
    }

    private sealed class Api : IBrowserApi
    {
        private readonly HashSet<string> _signedIn = [];

        public SiteSignInResult Result { get; set; } = SiteSignInResult.SignedIn;

        public Exception? Failure { get; set; }

        public string? AnswersAs { get; set; }

        public bool IsSignedIn(string domain) => _signedIn.Contains(domain);

        public Task<SiteSignInResult> SignInAsync(string domain, string userName, string password, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            if (Result == SiteSignInResult.SignedIn)
            {
                _signedIn.Add(domain);
            }

            return Task.FromResult(Result);
        }

        public void SignOut(string domain) => _signedIn.Remove(domain);

        public Task<SchemaBrowserContext> GetContextAsync(string domain, CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult(new SchemaBrowserContext("Site " + domain, AnswersAs ?? domain)) : throw Failure;

        public Task<SchemaCatalogList> GetSchemasAsync(string domain, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SchemaCatalogList([new("urn:post", "content", "Post", 3), new("urn:role", "identity", "Role", 1), new("urn:user", "identity", "User", 5)], ["urn:broken"]));

        public Task<SchemaDetail?> GetSchemaAsync(string domain, string schemaId, CancellationToken cancellationToken = default) =>
            Failure is not null ? throw Failure
            : Task.FromResult(schemaId == "urn:none" ? null : new SchemaDetail(schemaId, "a", schemaId, null, [], [], []));
    }

    private static async Task<SchemaBrowserViewModel> SignedInAsync(BrowserShellViewModel shell)
    {
        var signIn = Assert.IsType<SiteSignInViewModel>(shell.Current);
        signIn.UserName = "ann";
        signIn.Password = "pw";
        await signIn.SignInAsync();
        await shell.Opened;
        return Assert.IsType<SchemaBrowserViewModel>(shell.Current);
    }

    [Fact]
    public async Task The_configured_domains_are_listed_and_the_first_asks_for_a_sign_in()
    {
        var shell = new BrowserShellViewModel(new Api(), new Store("one.example", "two.example"));

        await shell.LoadAsync();

        Assert.Equal(["one.example", "two.example"], shell.Domains);
        Assert.Equal("one.example", Assert.IsType<SiteSignInViewModel>(shell.Current).Domain);
    }

    [Fact]
    public async Task A_domain_is_added_once_saved_and_chosen_and_nonsense_is_refused()
    {
        var store = new Store();
        var shell = new BrowserShellViewModel(new Api(), store);
        await shell.LoadAsync();
        Assert.Null(shell.Current);

        shell.NewDomain = "not a domain";
        await shell.AddAsync();
        Assert.NotNull(shell.Error);
        Assert.Empty(store.Saved);

        shell.NewDomain = "https://One.Example/";
        await shell.AddAsync();
        shell.NewDomain = "one.example";
        await shell.AddAsync();

        Assert.Equal(["one.example"], store.Saved);
        Assert.Equal(("one.example", string.Empty, (string?)null), (shell.SelectedDomain, shell.NewDomain, shell.Error));
        Assert.IsType<SiteSignInViewModel>(shell.Current);
    }

    [Fact]
    public async Task Removing_a_domain_signs_out_and_forgets_it()
    {
        var api = new Api();
        var store = new Store("one.example");
        var shell = new BrowserShellViewModel(api, store);
        await shell.LoadAsync();
        await SignedInAsync(shell);

        await shell.RemoveAsync();

        Assert.False(api.IsSignedIn("one.example"));
        Assert.Empty(store.Saved);
        Assert.Null(shell.Current);
    }

    [Theory]
    [InlineData(SiteSignInResult.Refused, "refused")]
    [InlineData(SiteSignInResult.MustChangePassword, "Change your password on one.example")]
    public async Task A_sign_in_that_fails_says_why_and_clears_the_password(SiteSignInResult result, string says)
    {
        var signIn = new SiteSignInViewModel(new Api { Result = result }, "one.example") { UserName = "ann", Password = "pw" };

        await signIn.SignInAsync();

        Assert.Contains(says, signIn.Error);
        Assert.Empty(signIn.Password);
    }

    [Fact]
    public async Task Each_domain_keeps_its_own_browser_while_signed_in()
    {
        var shell = new BrowserShellViewModel(new Api(), new Store("one.example", "two.example"));
        await shell.LoadAsync();
        var one = await SignedInAsync(shell);
        await one.OpenAsync("urn:post");

        shell.SelectedDomain = "two.example";
        Assert.IsType<SiteSignInViewModel>(shell.Current);
        shell.SelectedDomain = "one.example";

        Assert.Same(one, shell.Current);
        Assert.Equal("urn:post", one.Detail!.Id);
    }

    [Fact]
    public async Task The_schemas_are_grouped_by_area_filtered_and_unreadable_ones_are_named()
    {
        var browser = new SchemaBrowserViewModel(new Api(), "one.example");

        await browser.LoadAsync();

        Assert.Equal("Site one.example", browser.SiteTitle);
        Assert.Equal(["content", "identity"], browser.Areas.Select(a => a.Name));
        Assert.Contains("urn:broken", browser.Notice);
        browser.Filter = "ro";
        Assert.Equal(["Role"], browser.Areas.SelectMany(a => a.Schemas).Select(s => s.Name));
        browser.Filter = "IDENT";
        Assert.Equal(["Role", "User"], browser.Areas.SelectMany(a => a.Schemas).Select(s => s.Name));
        browser.Filter = "";
        Assert.Equal(3, browser.Areas.Sum(a => a.Schemas.Count));
    }

    [Fact]
    public async Task A_site_that_answers_under_another_name_says_so()
    {
        var browser = new SchemaBrowserViewModel(new Api { AnswersAs = "other.example" }, "one.example");

        await browser.LoadAsync();

        Assert.Contains("answered as other.example", browser.SiteTitle);
    }

    [Fact]
    public async Task Followed_links_can_be_walked_back()
    {
        var browser = new SchemaBrowserViewModel(new Api(), "one.example");

        await browser.OpenAsync("urn:post");
        Assert.False(browser.CanGoBack);
        await browser.OpenAsync("urn:role");
        await browser.OpenAsync("urn:role");
        await browser.OpenAsync("urn:none");
        Assert.NotNull(browser.Error);
        Assert.Equal("urn:role", browser.Detail!.Id);

        await browser.BackCommand.RunAsync();

        Assert.Equal("urn:post", browser.Detail!.Id);
        Assert.False(browser.CanGoBack);
    }

    [Fact]
    public async Task A_lapsed_session_returns_to_that_domains_sign_in()
    {
        var api = new Api();
        var shell = new BrowserShellViewModel(api, new Store("one.example"));
        await shell.LoadAsync();
        var browser = await SignedInAsync(shell);
        api.Failure = new ApiException(HttpStatusCode.Unauthorized, "ended");

        await browser.OpenAsync("urn:post");

        var signIn = Assert.IsType<SiteSignInViewModel>(shell.Current);
        Assert.Contains("session has ended", signIn.Error);
        Assert.False(api.IsSignedIn("one.example"));
    }

    [Fact]
    public async Task Signing_out_returns_to_the_sign_in()
    {
        var api = new Api();
        var shell = new BrowserShellViewModel(api, new Store("one.example"));
        await shell.LoadAsync();
        var browser = await SignedInAsync(shell);

        await browser.SignOutCommand.RunAsync();

        Assert.IsType<SiteSignInViewModel>(shell.Current);
        Assert.False(api.IsSignedIn("one.example"));
    }

    [Fact]
    public async Task A_domain_list_that_cannot_be_read_says_so_and_an_unforeseen_failure_is_raised_for_the_log()
    {
        var broken = new BrowserShellViewModel(new Api(), new Store { Failure = new InvalidOperationException("The settings file could not be read.") });
        await broken.LoadAsync();
        Assert.Equal("The settings file could not be read.", broken.Error);

        var api = new Api();
        var shell = new BrowserShellViewModel(api, new Store("one.example"));
        var logged = new List<Exception>();
        shell.Failed += (_, error) => logged.Add(error);
        await shell.LoadAsync();
        var browser = await SignedInAsync(shell);
        api.Failure = new NotSupportedException("detail");

        await browser.OpenAsync("urn:post");

        Assert.Single(logged);
        Assert.DoesNotContain("detail", browser.Error);
    }
}
