using System.Text.Json;
using Adventures.Entities;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// Two parts of BlogEngine's Controls and Advanced settings. The tracking script is NOT free-form code (a script on every public page can read an
/// administrator's sign-in from browser storage, so letting any settings editor paste code would be a way to take over every other administrator): it is
/// one https address whose host the operator has allowed. The export is a plain JSON backup of posts and pages.
/// </summary>
public sealed class TrackingAndExportTests
{
    private static readonly string[] Allowed = ["analytics.example.org", "Plausible.IO"];

    [Theory]
    [InlineData("https://analytics.example.org/script.js", "https://analytics.example.org/script.js")]
    [InlineData("  https://plausible.io/js/script.js  ", "https://plausible.io/js/script.js")]
    [InlineData("HTTPS://ANALYTICS.EXAMPLE.ORG/a.js", "https://analytics.example.org/a.js")]
    [InlineData("", "")]
    [InlineData("http://analytics.example.org/script.js", "")]
    [InlineData("https://evil.example.net/script.js", "")]
    [InlineData("https://analytics.example.org.evil.net/script.js", "")]
    [InlineData("https://analytics.example.org@evil.net/script.js", "")]
    [InlineData("https://user:pw@analytics.example.org/script.js", "")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("data:text/javascript,alert(1)", "")]
    [InlineData("//analytics.example.org/script.js", "")]
    [InlineData("https://analytics.example.org:8443/script.js", "")]
    [InlineData("https://analytics.example.org/a.js\"><script>alert(1)</script>", "")]
    [InlineData("not a url", "")]
    public void TheTrackingAddress_IsHttpsOnAnAllowedHost_ElseNothing(string value, string expected) =>
        Assert.Equal(expected, SiteSettings.AllowedScriptUrl(value, Allowed));

    [Fact]
    public void WithNoAllowedHost_TheFeatureIsOff()
    {
        Assert.Equal(string.Empty, SiteSettings.AllowedScriptUrl("https://analytics.example.org/script.js", []));
        Assert.Equal(string.Empty, SiteSettings.AllowedScriptUrl("https://analytics.example.org/script.js", null));
    }

    private static EntityDataModel Row(string id, params (string Name, string? Value)[] values) =>
        new(id, values.ToDictionary(v => v.Name, v => v.Value));

    private static EntityDataModel RowWithTerms(string id, Dictionary<string, string?> values, Dictionary<string, string> display) =>
        new(id, values, display);

    [Fact]
    public void APostIsExportedWithItsTextCategoriesAndTags_AndADraftSaysSo()
    {
        var post = RowWithTerms(
            "p1",
            new() { ["Title"] = "Hello", ["Slug"] = "hello", ["Content"] = "# Hi", ["ContentFormat"] = "Markdown", ["IsPublished"] = "true", ["DatePublished"] = "2026-05-25T10:00:00+00:00", ["EnableComments"] = "true", ["Description"] = "d" },
            new() { ["Categories"] = "Help, News", ["Tags"] = "mcp" });
        var draft = Row("p2", ("Title", "Draft"), ("Slug", "draft"), ("IsPublished", "false"));

        var export = ContentExport.Build([post, draft], [], new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(("adventures-content-export", 1), (export.Format, export.Version));
        var first = export.Posts[0];
        Assert.Equal(("Hello", "hello", "# Hi", "Markdown", true), (first.Title, first.Slug, first.Content, first.ContentFormat, first.IsPublished));
        Assert.Equal(["Help", "News"], first.Categories);
        Assert.Equal(["mcp"], first.Tags);
        Assert.False(export.Posts[1].IsPublished);
    }

    [Fact]
    public void AProposedRevision_IsNotAPostOfItsOwn_AndIsLeftOut()
    {
        var real = Row("p1", ("Title", "Real"), ("Slug", "real"), ("IsPublished", "true"));
        var revision = Row("p2", ("Title", "Real (revision)"), ("Slug", "real-rev"), ("RevisionOf", "p1"));

        var export = ContentExport.Build([real, revision], [], DateTimeOffset.UtcNow);

        Assert.Equal("Real", Assert.Single(export.Posts).Title);
    }

    [Fact]
    public void APageKeepsItsPlaceInTheTree_ParentSortOrderAndTheFlags()
    {
        var page = RowWithTerms(
            "g1",
            new() { ["Title"] = "Angular", ["Slug"] = "angular", ["Content"] = "x", ["IsPublished"] = "true", ["SortOrder"] = "10", ["ShowInList"] = "true", ["IsFrontPage"] = "false", ["Keywords"] = "ng" },
            new() { ["Parent"] = "Article" });

        var export = ContentExport.Build([], [page], DateTimeOffset.UtcNow);

        var exported = Assert.Single(export.Pages);
        Assert.Equal(("Angular", "Article", 10, true, false), (exported.Title, exported.Parent, exported.SortOrder, exported.ShowInList, exported.IsFrontPage));
    }

    [Fact]
    public void TheExport_IsJson_WithNoUserOrSecretFields()
    {
        var post = Row("p1", ("Title", "T"), ("Slug", "t"), ("Author", "some-user-id"), ("PasswordHash", "should-never-appear"));

        var json = JsonSerializer.Serialize(ContentExport.Build([post], [], DateTimeOffset.UtcNow));

        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("should-never-appear", json);
        Assert.DoesNotContain("some-user-id", json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(1, parsed.RootElement.GetProperty("Version").GetInt32());
    }
}
