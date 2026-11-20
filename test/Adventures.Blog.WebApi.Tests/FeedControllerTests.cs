using Adventures.Blog;
using Adventures.Entities;
using Adventures.Blog.WebApi;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>The RSS feed is public and lists published posts only; the link base is the tenant's own domain, never the API's host.</summary>
public sealed class FeedControllerTests
{
    private sealed class FakeContent(params PublicPostSummary[] posts) : IPublicContent
    {
        public bool? IncludeDrafts;
        public int? PageSizeAsked;

        public Task<PublicPostList> ListPostsAsync(int page, int pageSize, string? category, string? tag, CancellationToken cancellationToken = default, bool includeDrafts = false)
        {
            IncludeDrafts = includeDrafts;
            PageSizeAsked = pageSize;
            return Task.FromResult(new PublicPostList(posts, page, pageSize, posts.Length));
        }

        public Task<PublicPost?> GetPostAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult<PublicPost?>(null);

        public Task<PublicPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult<PublicPage?>(null);

        public Task<PublicPage?> GetFrontPageAsync(CancellationToken cancellationToken = default) => Task.FromResult<PublicPage?>(null);

        public Task<IReadOnlyList<PublicMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult<IReadOnlyList<PublicMenuItem>>([]);

        public Task<IReadOnlyList<PublicTerm>> GetCategoriesAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult<IReadOnlyList<PublicTerm>>([]);

        public Task<IReadOnlyList<PublicTerm>> GetTagsAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) => Task.FromResult<IReadOnlyList<PublicTerm>>([]);
    }

    private sealed class FakeSettings : ISiteSettings
    {
        public Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PublicSiteSettings("Site", "Tag", 10));

        public Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
    }

    private sealed class FakeTenant(string? domain) : IRequestTenant
    {
        public CurrentTenant Current { get; } = new(null, null, domain, string.Empty, TenantMatch.Domain);
    }

    private static PublicPostSummary Post(string slug) => new("T " + slug, slug, "d", "2026-05-25T10:00:00+00:00", "Bill", [], []);

    [Fact]
    public async Task ServesRssXml_OfPublishedPostsOnly_WithLinksOnTheTenantDomain()
    {
        var content = new FakeContent(Post("one"), Post("two"));
        var controller = new FeedController(content, new FakeSettings(), new FakeTenant("blog.example.org"));

        var result = Assert.IsType<ContentResult>(await controller.Get(CancellationToken.None));

        Assert.Equal("application/rss+xml; charset=utf-8", result.ContentType);
        Assert.False(content.IncludeDrafts);
        Assert.Contains("https://blog.example.org/post/one", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("api.", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABareTenantDomain_IsLinkedAsWww()
    {
        var controller = new FeedController(new FakeContent(Post("one")), new FakeSettings(), new FakeTenant("example.org"));

        var result = Assert.IsType<ContentResult>(await controller.Get(CancellationToken.None));

        Assert.Contains("https://www.example.org/post/one", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.org", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsksForAtMostTwentyPosts()
    {
        var content = new FakeContent();
        var controller = new FeedController(content, new FakeSettings(), new FakeTenant("blog.example.org"));

        await controller.Get(CancellationToken.None);

        Assert.Equal(FeedController.MaxItems, content.PageSizeAsked);
        Assert.True(FeedController.MaxItems <= 20);
    }

    [Fact]
    public async Task WithNoTenantDomain_TheFeedIsNotFound_RatherThanPointingAtAGuessedHost()
    {
        var controller = new FeedController(new FakeContent(Post("one")), new FakeSettings(), new FakeTenant(null));

        Assert.IsType<NotFoundResult>(await controller.Get(CancellationToken.None));
    }
}
