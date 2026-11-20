using Adventures.Tools;
using Xunit;

namespace Adventures.Tools.Tests;

/// <summary>Finding a tool: each tool answers CanHandle, the selector walks the chain, and a fallback tool always ends it.</summary>
public class ToolSelectorTests
{
    private static AppTool Tool(string name, string description, string scope = "content.draft") =>
        new(name, description, "{}", scope, "GET", $"api/{name}");

    private sealed class WikiTool() : AppTool("wiki_lookup", "Looks things up", "{}", "posts.read", "GET", "api/wiki")
    {
        public override bool CanHandle(string searchKey) => searchKey.Contains("wiki", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedGuesser(IAppTool? answer) : IToolGuesser
    {
        public IAppTool? Guess(string searchKey, IReadOnlyList<IAppTool> available) => answer;
    }

    private static readonly AppTool CreateDraft = Tool("create_draft_post", "Creates a draft blog post");
    private static readonly AppTool ListTags = Tool("list_tags", "Lists the tags of the blog");

    [Fact]
    public void TheNameMatchesExactly()
    {
        Assert.True(CreateDraft.CanHandle("create_draft_post"));
        Assert.True(CreateDraft.CanHandle("CREATE_DRAFT_POST"));
    }

    [Fact]
    public void WordsFromTheDescriptionMatchLikeAGrep()
    {
        Assert.True(CreateDraft.CanHandle("draft"));
        Assert.True(CreateDraft.CanHandle("blog post"));
        Assert.False(CreateDraft.CanHandle("tags"));
    }

    [Fact]
    public void ASmallMisspellingOfTheNameStillMatches()
    {
        Assert.True(ListTags.CanHandle("list_tgas"));
        Assert.False(ListTags.CanHandle("delete_everything"));
    }

    [Fact]
    public void AToolMayDecideForItselfWhetherItMatches()
    {
        Assert.True(new WikiTool().CanHandle("search the Wiki"));
        Assert.False(new WikiTool().CanHandle("tags"));
    }

    [Fact]
    public void TheFirstToolThatHandlesTheKeyWins()
    {
        var selector = new ToolSelector();

        var found = selector.Select("tags", [CreateDraft, ListTags, new WikiTool()]);

        Assert.Same(ListTags, found);
    }

    [Fact]
    public void WhenNobodyHandlesTheKeyTheFallbackAnswers()
    {
        var found = new ToolSelector().Select("zzzz qqqq", [CreateDraft, ListTags]);

        Assert.IsType<DefaultFallbackTool>(found);
    }

    [Fact]
    public void TheFallbackAlwaysHandlesAndIsNeverAheadOfARealTool()
    {
        var fallback = new DefaultFallbackTool();
        Assert.True(fallback.CanHandle(""));
        Assert.True(fallback.CanHandle("anything at all"));

        var found = new ToolSelector().Select("tags", [fallback, ListTags]);

        Assert.Same(ListTags, found);
    }

    [Fact]
    public void AnAiGuessIsUsedWhenNoToolHandlesTheKey()
    {
        var found = new ToolSelector(new FixedGuesser(ListTags)).Select("zzzz qqqq", [CreateDraft, ListTags]);

        Assert.Same(ListTags, found);
    }

    [Fact]
    public void AGuessNeverOverridesATool_ThatHandlesTheKey()
    {
        var found = new ToolSelector(new FixedGuesser(CreateDraft)).Select("tags", [CreateDraft, ListTags]);

        Assert.Same(ListTags, found);
    }

    [Fact]
    public void AGuessOutsideTheAvailableToolsIsIgnored()
    {
        var found = new ToolSelector(new FixedGuesser(Tool("secret_tool", "not offered"))).Select("zzzz", [CreateDraft]);

        Assert.IsType<DefaultFallbackTool>(found);
    }

    [Fact]
    public void ANullOrBlankKeyReachesTheFallbackInsteadOfThrowing()
    {
        Assert.IsType<DefaultFallbackTool>(new ToolSelector().Select("   ", [CreateDraft]));
        Assert.IsType<DefaultFallbackTool>(new ToolSelector().Select(null!, [CreateDraft]));
    }
}
