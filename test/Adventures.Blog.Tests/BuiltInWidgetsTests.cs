using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>The widgets that ship with the blog: a text box, a tag cloud and a category list. Names are encoded and links are plain addresses, so a name with markup in it cannot inject any.</summary>
public sealed class BuiltInWidgetsTests
{
    private sealed class FakeContent(IReadOnlyList<PublicTerm> tags, IReadOnlyList<PublicTerm> categories) : IPublicContent
    {
        public Task<PublicPostList> ListPostsAsync(int page, int pageSize, string? category, string? tag, CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<PublicPost?> GetPostAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<PublicPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<PublicPage?> GetFrontPageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PublicMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<IReadOnlyList<PublicTerm>> GetCategoriesAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult(categories);

        public Task<IReadOnlyList<PublicTerm>> GetTagsAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult(tags);
    }

    private static WidgetContext Context(params (string Key, string Value)[] settings) =>
        new("right", settings.ToDictionary(s => s.Key, s => s.Value), null);

    [Fact]
    public async Task TheTextBox_ReturnsItsText_AsTheHtmlTheOwnerWrote()
    {
        var html = await new TextBoxWidget().RenderAsync(Context(("Content", "<p>Hello <b>there</b></p>")), CancellationToken.None);

        Assert.Equal("<p>Hello <b>there</b></p>", html);
    }

    [Fact]
    public async Task TheTagCloud_DrawsEachTagAsAPillWithItsCountAndLink_MostUsedFirst()
    {
        var widget = new TagCloudWidget(new FakeContent([new PublicTerm("angular", 5), new PublicTerm("mcp", 2)], []));

        var html = await widget.RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);

        Assert.Contains("<ul class=\"tag-cloud\"", html);
        Assert.Contains("<a class=\"tag-cloud__tag tag-cloud__size-5\" href=\"/tag/angular\" title=\"5 posts\">angular<span class=\"tag-cloud__count\">5</span></a>", html);
        Assert.Contains("tag-cloud__size-1", html);
        Assert.True(html.IndexOf("angular", StringComparison.Ordinal) < html.IndexOf("mcp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheTagCloud_HidesTagsUsedByFewerPostsThanTheMinimum_TwoByDefault()
    {
        var content = new FakeContent([new PublicTerm("popular", 6), new PublicTerm("pair", 2), new PublicTerm("loner", 1)], []);

        var byDefault = await new TagCloudWidget(content).RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);
        var showAll = await new TagCloudWidget(content).RenderAsync(Context(("Max", "30"), ("Min", "1")), CancellationToken.None);
        var strict = await new TagCloudWidget(content).RenderAsync(Context(("Max", "30"), ("Min", "5")), CancellationToken.None);

        Assert.DoesNotContain("loner", byDefault);
        Assert.Contains("pair", byDefault);
        Assert.Contains("loner", showAll);
        Assert.DoesNotContain("pair", strict);
        Assert.Contains("popular", strict);
    }

    [Fact]
    public async Task TheTagCloud_SizesEachTagByHowMuchItIsUsed_AndSingleSizedWhenAllAreEqual()
    {
        var content = new FakeContent([new PublicTerm("big", 10), new PublicTerm("mid", 6), new PublicTerm("small", 2)], []);

        var html = await new TagCloudWidget(content).RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);
        var equal = await new TagCloudWidget(new FakeContent([new PublicTerm("a", 3), new PublicTerm("b", 3)], [])).RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);

        Assert.Contains("tag-cloud__size-5\" href=\"/tag/big\"", html);
        Assert.Contains("tag-cloud__size-3\" href=\"/tag/mid\"", html);
        Assert.Contains("tag-cloud__size-1\" href=\"/tag/small\"", html);
        Assert.Equal(2, equal.Split("tag-cloud__size-3").Length - 1);
    }

    [Fact]
    public async Task ANameWithMarkupOrSpaces_IsEncoded_InTheTextAndInTheAddress()
    {
        var widget = new TagCloudWidget(new FakeContent([new PublicTerm("<script>x</script> & c#", 3)], []));

        var html = await widget.RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("/tag/%3Cscript%3Ex%3C%2Fscript%3E%20%26%20c%23", html);
    }

    [Fact]
    public async Task TheTagCloud_ShowsAtMostTheChosenNumber()
    {
        var terms = Enumerable.Range(1, 10).Select(i => new PublicTerm("t" + i, 20 - i)).ToList();

        var html = await new TagCloudWidget(new FakeContent(terms, [])).RenderAsync(Context(("Max", "3"), ("Min", "2")), CancellationToken.None);

        Assert.Equal(3, html.Split("<li class=\"tag-cloud__item\">").Length - 1);
    }

    [Fact]
    public async Task TheCategoryList_LinksToTheCategoryPages()
    {
        var html = await new CategoryListWidget(new FakeContent([], [new PublicTerm("Help", 4)])).RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);

        Assert.Contains("<a href=\"/category/Help\">Help</a>", html);
    }

    [Fact]
    public async Task WithNothingToShow_TheWidgetsAnswerNothing_SoNoEmptyBoxIsDrawn()
    {
        Assert.Equal(string.Empty, await new TagCloudWidget(new FakeContent([], [])).RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None));
        Assert.Equal(string.Empty, await new TextBoxWidget().RenderAsync(Context(("Content", "  ")), CancellationToken.None));
    }

    [Fact]
    public void AddBlog_RegistersTheBuiltInWidgets_UnderABuiltInPlugin()
    {
        var services = new ServiceCollection();
        services.AddBlog();

        var keys = services.Where(d => d.ServiceType == typeof(WidgetRegistration)).Select(d => (WidgetRegistration)d.ImplementationInstance!).ToList();

        Assert.Equal(["text-box", "tag-cloud", "category-list", "administration", "post-list", "page-list"], keys.Select(k => k.Definition.Key));
        Assert.All(keys, k => Assert.Equal(WidgetsPlugin.Key, k.PluginKey));
        Assert.Contains(services, d => d.ServiceType == typeof(PluginDefinition) && ((PluginDefinition)d.ImplementationInstance!).Key == WidgetsPlugin.Key);
    }

    [Fact]
    public async Task TheCloudsPills_SurviveTheSanitiser_WithTheirClassesLinksAndCounts()
    {
        var html = await new TagCloudWidget(new FakeContent([new PublicTerm("angular", 5), new PublicTerm("mcp", 2)], [])).RenderAsync(Context(("Max", "30"), ("Min", "2")), CancellationToken.None);

        var cleaned = HtmlContent.Sanitize(html);

        Assert.Contains("class=\"tag-cloud__tag tag-cloud__size-5\"", cleaned);
        Assert.Contains("href=\"/tag/angular\"", cleaned);
        Assert.Contains("<span class=\"tag-cloud__count\">5</span>", cleaned);
        Assert.Contains("title=\"5 posts\"", cleaned);
    }
}

/// <summary>The Administration widget, as BlogEngine had it: the signed-in administrator's links, shown only to those who may see the admin area.</summary>
public sealed class AdministrationWidgetTests
{
    private static readonly WidgetContext Context = new("right", new Dictionary<string, string>(), null);

    [Fact]
    public async Task TheAdministrationWidget_ListsTheAdminLinks_AsPlainAddresses()
    {
        var html = await new AdministrationWidget().RenderAsync(Context, CancellationToken.None);

        foreach (var (label, href) in new[]
        {
            ("Dashboard", "/admin/dashboard"), ("Content", "/admin/posts"), ("Custom", "/admin/plugins"), ("Users", "/admin/users"),
            ("Settings", "/admin/settings"), ("Profile", "/profile"), ("Change password", "/profile#password"), ("Log off", "/logout"),
        })
        {
            Assert.Contains($"<a href=\"{href}\">{label}</a>", html);
        }

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAdministrationWidget_IsOfferedOnlyToThoseWithAdminAccess()
    {
        Assert.Equal("admin.access", AdministrationWidget.Definition.RequiredRight);
        Assert.Equal("administration", AdministrationWidget.Definition.Key);
    }
}

/// <summary>The Post List and Page List widgets (from the legacy widget set): plain lists of links, names encoded so markup in a title injects nothing.</summary>
public sealed class ListWidgetsTests
{
    private sealed class FakeContent(IReadOnlyList<PublicPostSummary> posts, IReadOnlyList<PublicMenuItem> menu) : IPublicContent
    {
        public int? AskedPageSize { get; private set; }

        public Task<PublicPostList> ListPostsAsync(int page, int pageSize, string? category, string? tag, CancellationToken cancellationToken = default, bool includeDrafts = false)
        {
            AskedPageSize = pageSize;
            return Task.FromResult(new PublicPostList(posts.Take(pageSize).ToList(), page, pageSize, posts.Count));
        }

        public Task<PublicPost?> GetPostAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<PublicPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<PublicPage?> GetFrontPageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PublicMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult(menu);

        public Task<IReadOnlyList<PublicTerm>> GetCategoriesAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();

        public Task<IReadOnlyList<PublicTerm>> GetTagsAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => throw new NotSupportedException();
    }

    private static PublicPostSummary Post(string title, string slug) => new(title, slug, null, "2026-11-20", "Bill", [], []);

    private static WidgetContext Context(string max = "5") => new("right", new Dictionary<string, string> { ["Max"] = max }, null);

    [Fact]
    public async Task ThePostList_LinksTheNewestPosts_ByAddress_WithTitlesEncoded()
    {
        var content = new FakeContent([Post("One <b>x</b>", "one"), Post("Two", "two"), Post("Three", "three")], []);

        var html = await new PostListWidget(content).RenderAsync(Context("2"), CancellationToken.None);

        Assert.Equal(2, content.AskedPageSize);
        Assert.Contains("<a href=\"/post/one\">One &lt;b&gt;x&lt;/b&gt;</a>", html);
        Assert.Contains("<a href=\"/post/two\">Two</a>", html);
        Assert.DoesNotContain("three", html);
    }

    [Fact]
    public async Task ThePostList_IsEmpty_WhenThereAreNoPosts() =>
        Assert.Equal(string.Empty, await new PostListWidget(new FakeContent([], [])).RenderAsync(Context(), CancellationToken.None));

    [Fact]
    public async Task ThePageList_LinksThePagesOfTheMenu_WithTheirSubPages()
    {
        var content = new FakeContent([], [new PublicMenuItem("About", "about", [new PublicMenuItem("Team", "team", [])]), new PublicMenuItem("Contact", "contact", [])]);

        var html = await new PageListWidget(content).RenderAsync(Context(), CancellationToken.None);

        Assert.Contains("<a href=\"/page/about\">About</a>", html);
        Assert.Contains("<ul><li><a href=\"/page/team\">Team</a></li></ul>", html);
        Assert.Contains("<a href=\"/page/contact\">Contact</a>", html);
    }

    [Fact]
    public async Task ThePageList_IsEmpty_WhenThereAreNoPages() =>
        Assert.Equal(string.Empty, await new PageListWidget(new FakeContent([], [])).RenderAsync(Context(), CancellationToken.None));
}
