using System.Xml.Linq;
using Adventures.Blog;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>The RSS 2.0 feed of the latest published posts: well-formed XML whatever the titles contain, absolute links, no author e-mail, no ids.</summary>
public sealed class RssFeedTests
{
    private static readonly PublicSiteSettings Site = new("My Blog", "Notes & ideas", 10);

    private static PublicPostSummary Post(string title, string slug, string? date = "2026-05-25T14:30:00+00:00", string? description = "About it", params string[] categories) =>
        new(title, slug, description, date, "Bill", categories, ["tagged"]);

    private static XElement Channel(string xml) => XDocument.Parse(xml).Root!.Element("channel")!;

    [Fact]
    public void BuildsAnRss2Channel_WithTheSiteTitleTaglineAndAbsoluteSiteLink()
    {
        var xml = RssFeed.Build(Site, "https://example.org", [Post("Hello", "hello")]);

        var root = XDocument.Parse(xml).Root!;
        Assert.Equal("rss", root.Name.LocalName);
        Assert.Equal("2.0", root.Attribute("version")!.Value);
        var channel = root.Element("channel")!;
        Assert.Equal("My Blog", channel.Element("title")!.Value);
        Assert.Equal("Notes & ideas", channel.Element("description")!.Value);
        Assert.Equal("https://example.org", channel.Element("link")!.Value);
    }

    [Fact]
    public void EachItemLinksToTheCurrentPostAddress_AndCarriesAnRfc822Date_AndItsCategories()
    {
        var xml = RssFeed.Build(Site, "https://example.org/", [Post("Hello", "hello", categories: ["News", "AI"])]);

        var item = Channel(xml).Element("item")!;
        Assert.Equal("https://example.org/post/hello", item.Element("link")!.Value);
        Assert.Equal("https://example.org/post/hello", item.Element("guid")!.Value);
        Assert.Equal("true", item.Element("guid")!.Attribute("isPermaLink")!.Value);
        Assert.Equal("Sun, 24 May 2026 14:30:00 GMT".Length, item.Element("pubDate")!.Value.Length);
        Assert.Equal("Mon, 25 May 2026 14:30:00 GMT", item.Element("pubDate")!.Value);
        Assert.Equal(["News", "AI"], item.Elements("category").Select(c => c.Value));
    }

    [Fact]
    public void MarkupAndAmpersandsInTitlesAndDescriptions_StayWellFormedAndComeBackAsText()
    {
        var xml = RssFeed.Build(Site, "https://example.org", [Post("Tom & <Jerry> \"quoted\"", "tom", description: "a < b && c > d")]);

        var item = Channel(xml).Element("item")!;
        Assert.Equal("Tom & <Jerry> \"quoted\"", item.Element("title")!.Value);
        Assert.Equal("a < b && c > d", item.Element("description")!.Value);
    }

    [Fact]
    public void ThePostSlugIsEscapedInTheLink_SoItCannotBreakOutOfTheAddress()
    {
        var xml = RssFeed.Build(Site, "https://example.org", [Post("Odd", "a b/c?d")]);

        Assert.Equal("https://example.org/post/a%20b%2Fc%3Fd", Channel(xml).Element("item")!.Element("link")!.Value);
    }

    [Fact]
    public void AnItemWithNoDateOrDescription_IsStillValid_AndNoAuthorIsPublished()
    {
        var xml = RssFeed.Build(Site, "https://example.org", [Post("Bare", "bare", date: null, description: null)]);

        var item = Channel(xml).Element("item")!;
        Assert.Null(item.Element("pubDate"));
        Assert.Null(item.Element("description"));
        Assert.Null(item.Element("author"));
        Assert.DoesNotContain("Bill", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftsAreNeverPublished_EvenIfTheCallerPassesOne()
    {
        var draft = Post("Secret", "secret") with { IsDraft = true };

        var xml = RssFeed.Build(Site, "https://example.org", [draft, Post("Public", "public")]);

        Assert.Equal(["Public"], Channel(xml).Elements("item").Select(i => i.Element("title")!.Value));
    }
}
