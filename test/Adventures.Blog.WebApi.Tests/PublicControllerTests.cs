// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Adventures.Entities;
using Adventures.WebApi;
using Adventures.Security;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Adventures.Blog;

using Adventures.Blog.WebApi;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>Drafts reach the reader only through a token that passes the administrator policy; an anonymous call, or a signed-in non-administrator, gets published content only.</summary>
public sealed class PublicControllerTests
{
    private sealed class RecordingContent : IPublicContent
    {
        public bool? PostsIncludeDrafts;
        public bool? PostIncludeDrafts;
        public bool? PageIncludeDrafts;
        public bool? MenuIncludeDrafts;

        public Task<PublicPostList> ListPostsAsync(int page, int pageSize, string? category, string? tag, CancellationToken cancellationToken = default, bool includeDrafts = false)
        {
            PostsIncludeDrafts = includeDrafts;
            return Task.FromResult(new PublicPostList([], 1, pageSize, 0));
        }

        public Task<PublicPost?> GetPostAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false)
        {
            PostIncludeDrafts = includeDrafts;
            return Task.FromResult<PublicPost?>(null);
        }

        public Task<PublicPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false)
        {
            PageIncludeDrafts = includeDrafts;
            return Task.FromResult<PublicPage?>(null);
        }

        public Task<PublicPage?> GetFrontPageAsync(CancellationToken cancellationToken = default) => Task.FromResult<PublicPage?>(null);

        public Task<IReadOnlyList<PublicMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default, bool includeDrafts = false)
        {
            MenuIncludeDrafts = includeDrafts;
            return Task.FromResult<IReadOnlyList<PublicMenuItem>>([]);
        }

        public Task<IReadOnlyList<PublicTerm>> GetCategoriesAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) =>
            Task.FromResult<IReadOnlyList<PublicTerm>>([]);

        public Task<IReadOnlyList<PublicTerm>> GetTagsAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) =>
            Task.FromResult<IReadOnlyList<PublicTerm>>([]);
    }

    private sealed class FakeAuthorization(bool succeeds) : IRightsAuthorizer
    {
        public string? RightAsked;

        public Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(succeeds ? Rights.AllNames : new HashSet<string>());

        public Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default)
        {
            RightAsked = right;
            return Task.FromResult(succeeds);
        }
    }

    private static (PublicController Controller, RecordingContent Content, FakeAuthorization Authorization) Build(bool signedIn, bool policyPasses)
    {
        var content = new RecordingContent();
        var authorization = new FakeAuthorization(policyPasses);
        var principal = signedIn
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        var controller = new PublicController(content, authorization)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, content, authorization);
    }

    [Fact]
    public async Task AnAdministrator_GetsDraftsOnEveryRead_AndTheRightAskedIsPostsRead()
    {
        var (controller, content, authorization) = Build(signedIn: true, policyPasses: true);

        await controller.Posts();
        await controller.Post("x", CancellationToken.None);
        await controller.Page("x", CancellationToken.None);
        await controller.Menu(CancellationToken.None);

        Assert.True(content.PostsIncludeDrafts);
        Assert.True(content.PostIncludeDrafts);
        Assert.True(content.PageIncludeDrafts);
        Assert.True(content.MenuIncludeDrafts);
        Assert.Equal(Rights.PostsRead, authorization.RightAsked);
    }

    [Fact]
    public async Task AVisitorGetsNoDrafts_AndTheAdminPolicyIsNotEvenAsked()
    {
        var (controller, content, authorization) = Build(signedIn: false, policyPasses: true);

        await controller.Posts();
        await controller.Post("x", CancellationToken.None);
        await controller.Menu(CancellationToken.None);

        Assert.False(content.PostsIncludeDrafts);
        Assert.False(content.PostIncludeDrafts);
        Assert.False(content.MenuIncludeDrafts);
        Assert.Null(authorization.RightAsked);
    }

    [Fact]
    public async Task ASignedInNonAdministrator_GetsNoDrafts()
    {
        var (controller, content, _) = Build(signedIn: true, policyPasses: false);

        await controller.Posts();
        await controller.Page("x", CancellationToken.None);

        Assert.False(content.PostsIncludeDrafts);
        Assert.False(content.PageIncludeDrafts);
    }

    /// <summary>
    /// A site with no front page is normal, not an error: the client asks on every navigation, and a 404 shows as a red failed request in the browser each time.
    /// So the answer is 204 No Content ("nothing pinned"), which the client reads as no front page and the browser does not log.
    /// </summary>
    [Fact]
    public async Task Home_WithNoFrontPage_IsNoContent_NotNotFound()
    {
        var (controller, _, _) = Build(signedIn: false, policyPasses: false);

        var result = await controller.Home(CancellationToken.None);

        Assert.IsType<NoContentResult>(result.Result);
    }
}
