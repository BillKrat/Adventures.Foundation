using Adventures.Common.Exceptions;
using Adventures.Entities;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// Basic search of the blog's own content (beta row 6, layer 1): published posts and pages only, every word must match, a title counts for more than the
/// text, and what comes back is plain text (a snippet around the match), never markup. The query is limited so an anonymous visitor cannot make the server
/// do unbounded work.
/// </summary>
public sealed class ContentSearchTests
{
    private static EntityDataModel Post(string id, string title, string content, bool published = true, string? tags = null, string? categories = null, string? description = null, string? revisionOf = null, string date = "2026-05-25T10:00:00+00:00")
    {
        var values = new Dictionary<string, string?>
        {
            ["Title"] = title, ["Slug"] = id, ["Content"] = content, ["IsPublished"] = published ? "true" : "false", ["DatePublished"] = date, ["Description"] = description, ["RevisionOf"] = revisionOf,
        };
        var display = new Dictionary<string, string>();
        if (tags is not null)
        {
            display["Tags"] = tags;
        }

        if (categories is not null)
        {
            display["Categories"] = categories;
        }

        return new EntityDataModel(id, values, display);
    }

    private static EntityDataModel Page(string id, string title, string content, bool published = true) =>
        new(id, new Dictionary<string, string?> { ["Title"] = title, ["Slug"] = id, ["Content"] = content, ["IsPublished"] = published ? "true" : "false" });

    private static SearchResult Search(string query, IEnumerable<EntityDataModel> posts, IEnumerable<EntityDataModel>? pages = null, int page = 1, int pageSize = 10) =>
        ContentSearch.Run(query, posts, pages ?? [], page, pageSize);

    [Fact]
    public void APostIsFoundByItsTitle_ItsTextOrItsTags_IgnoringCase()
    {
        var posts = new[] { Post("a", "Models and Agents", "text"), Post("b", "Other", "talks about MCP servers"), Post("c", "Third", "x", tags: "Angular, mcp") };

        Assert.Equal(["a"], Search("AGENTS", posts).Hits.Select(h => h.Slug));
        Assert.Equal(["b", "c"], Search("mcp", posts).Hits.Select(h => h.Slug).Order());
    }

    [Fact]
    public void EveryWordMustMatch_InAnyOfTheFields()
    {
        var posts = new[] { Post("a", "Angular signals", "about forms"), Post("b", "Angular", "about signals and forms"), Post("c", "React", "signals") };

        Assert.Equal(["a", "b"], Search("angular signals", posts).Hits.Select(h => h.Slug).Order());
        Assert.Empty(Search("angular vue", posts).Hits);
    }

    [Fact]
    public void DraftsRevisionsAndUnpublishedPages_AreNeverFound()
    {
        var posts = new[] { Post("live", "Secret plan", "x"), Post("draft", "Secret plan", "x", published: false), Post("rev", "Secret plan (revision)", "x", published: false, revisionOf: "live") };
        var pages = new[] { Page("p1", "Secret page", "x"), Page("p2", "Secret hidden", "x", published: false) };

        var hits = Search("secret", posts, pages).Hits;

        Assert.Equal(["live", "p1"], hits.Select(h => h.Slug).Order());
    }

    [Fact]
    public void AHitSaysWhetherItIsAPostOrAPage_AndCarriesItsDate()
    {
        var result = Search("angular", [Post("a", "Angular", "x")], [Page("g", "Angular guide", "x")]);

        Assert.Contains(result.Hits, h => h is { Kind: "post", Slug: "a", DatePublished: "2026-05-25T10:00:00+00:00" });
        Assert.Contains(result.Hits, h => h is { Kind: "page", Slug: "g" });
    }

    [Fact]
    public void ATitleHit_RanksAboveATextOnlyHit_AndANewerPostBreaksATie()
    {
        var posts = new[]
        {
            Post("text-only", "Something", "all about signals"),
            Post("title-new", "Signals", "x", date: "2026-06-01T00:00:00+00:00"),
            Post("title-old", "Signals", "x", date: "2026-01-01T00:00:00+00:00"),
        };

        Assert.Equal(["title-new", "title-old", "text-only"], Search("signals", posts).Hits.Select(h => h.Slug));
    }

    [Fact]
    public void MarkupIsNotSearched_NorShown()
    {
        var posts = new[] { Post("a", "T", "<div class=\"note\"><p>hello <strong>world</strong></p></div>"), Post("b", "T2", "# Heading\n\nsome *text* with `code` and [a link](https://example.org/x)") };

        Assert.Empty(Search("div", posts).Hits);
        Assert.Empty(Search("class", posts).Hits);
        var hit = Assert.Single(Search("world", posts).Hits);
        Assert.DoesNotContain('<', hit.Snippet);
        Assert.Contains("hello world", hit.Snippet);
        var markdown = Assert.Single(Search("heading", posts).Hits);
        Assert.DoesNotContain('#', markdown.Snippet);
        Assert.DoesNotContain('`', markdown.Snippet);
        Assert.DoesNotContain("https://example.org/x", markdown.Snippet);
        Assert.Contains("a link", markdown.Snippet);
    }

    [Fact]
    public void TheSnippet_IsShort_AndCentredOnTheFirstMatch()
    {
        var long1 = new string('a', 400) + " needle " + new string('b', 400);

        var hit = Assert.Single(Search("needle", [Post("a", "T", long1)]).Hits);

        Assert.Contains("needle", hit.Snippet);
        Assert.True(hit.Snippet.Length <= ContentSearch.SnippetLength + 2, hit.Snippet.Length.ToString());
        Assert.StartsWith("…", hit.Snippet);
        Assert.EndsWith("…", hit.Snippet);
    }

    [Fact]
    public void WithNoTextMatch_TheSnippetIsTheDescription()
    {
        var hit = Assert.Single(Search("angular", [Post("a", "Angular", "no match here", description: "A short summary.")]).Hits);

        Assert.Equal("A short summary.", hit.Snippet);
    }

    [Fact]
    public void ResultsArePaged_AndTheTotalIsTheWholeCount()
    {
        var posts = Enumerable.Range(1, 25).Select(n => Post($"p{n:00}", $"Match {n}", "x", date: $"2026-05-{n:00}T00:00:00+00:00")).ToList();

        var second = Search("match", posts, page: 2, pageSize: 10);

        Assert.Equal((25, 2, 10, 10), (second.Total, second.Page, second.PageSize, second.Hits.Count));
        Assert.Equal("p15", second.Hits[0].Slug);
        Assert.Empty(Search("match", posts, page: 9, pageSize: 10).Hits);
        Assert.Equal(ContentSearch.MaxPageSize, Search("match", posts, pageSize: 5000).PageSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void AQueryOfFewerThanTwoLetters_IsRefusedWithAReason(string query)
    {
        var error = Assert.Throws<BusinessRuleException>(() => Search(query, []));

        Assert.Contains("two", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALongQuery_IsRefused_AndOnlyTheFirstSixWordsCount()
    {
        Assert.Throws<BusinessRuleException>(() => Search(new string('x', ContentSearch.MaxQueryLength + 1), []));
        var posts = new[] { Post("a", "one two three four five six", "x") };

        Assert.Single(Search("one two three four five six seventhwordthatwillbeignored", posts).Hits);
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("(a|b)+")]
    [InlineData("[[")]
    [InlineData("%_")]
    [InlineData("<script>")]
    public void SpecialCharacters_AreJustText_NeverAPattern(string query)
    {
        var posts = new[] { Post("a", "Plain title", "plain content") };

        Assert.Empty(Search(query, posts).Hits);   // and, above all, no exception
    }

    [Fact]
    public void ARegexLookingQuery_MatchesOnlyTheLiteralText()
    {
        Assert.Equal(["a"], Search("c++ (beta)", [Post("a", "Learning C++ (beta) today", "x")]).Hits.Select(h => h.Slug));
    }
}
