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
    public async Task TheTagCloud_LinksEachTagToItsPage_WithItsCount_MostUsedFirst()
    {
        var widget = new TagCloudWidget(new FakeContent([new PublicTerm("angular", 5), new PublicTerm("mcp", 2)], []));

        var html = await widget.RenderAsync(Context(("Max", "30")), CancellationToken.None);

        Assert.Contains("<a href=\"/tag/angular\">angular</a>", html);
        Assert.Contains("(5)", html);
        Assert.True(html.IndexOf("angular", StringComparison.Ordinal) < html.IndexOf("mcp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANameWithMarkupOrSpaces_IsEncoded_InTheTextAndInTheAddress()
    {
        var widget = new TagCloudWidget(new FakeContent([new PublicTerm("<script>x</script> & c#", 1)], []));

        var html = await widget.RenderAsync(Context(("Max", "30")), CancellationToken.None);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("/tag/%3Cscript%3Ex%3C%2Fscript%3E%20%26%20c%23", html);
    }

    [Fact]
    public async Task TheTagCloud_ShowsAtMostTheChosenNumber()
    {
        var terms = Enumerable.Range(1, 10).Select(i => new PublicTerm("t" + i, 20 - i)).ToList();

        var html = await new TagCloudWidget(new FakeContent(terms, [])).RenderAsync(Context(("Max", "3")), CancellationToken.None);

        Assert.Equal(3, html.Split("<li>").Length - 1);
    }

    [Fact]
    public async Task TheCategoryList_LinksToTheCategoryPages()
    {
        var html = await new CategoryListWidget(new FakeContent([], [new PublicTerm("Help", 4)])).RenderAsync(Context(("Max", "30")), CancellationToken.None);

        Assert.Contains("<a href=\"/category/Help\">Help</a>", html);
    }

    [Fact]
    public async Task WithNothingToShow_TheWidgetsAnswerNothing_SoNoEmptyBoxIsDrawn()
    {
        Assert.Equal(string.Empty, await new TagCloudWidget(new FakeContent([], [])).RenderAsync(Context(("Max", "30")), CancellationToken.None));
        Assert.Equal(string.Empty, await new TextBoxWidget().RenderAsync(Context(("Content", "  ")), CancellationToken.None));
    }

    [Fact]
    public void AddBlog_RegistersTheThreeWidgets_UnderABuiltInPlugin()
    {
        var services = new ServiceCollection();
        services.AddBlog();

        var keys = services.Where(d => d.ServiceType == typeof(WidgetRegistration)).Select(d => (WidgetRegistration)d.ImplementationInstance!).ToList();

        Assert.Equal(["text-box", "tag-cloud", "category-list"], keys.Select(k => k.Definition.Key));
        Assert.All(keys, k => Assert.Equal(WidgetsPlugin.Key, k.PluginKey));
        Assert.Contains(services, d => d.ServiceType == typeof(PluginDefinition) && ((PluginDefinition)d.ImplementationInstance!).Key == WidgetsPlugin.Key);
    }
}
