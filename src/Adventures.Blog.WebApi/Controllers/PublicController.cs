// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.WebApi;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Blog;

namespace Adventures.Blog.WebApi;

/// <summary>
/// The read side of the site, anonymous by default: published posts and pages only (see <see cref="IPublicContent"/>). Nothing here
/// accepts a write, or returns an id or an e-mail address; page size is capped by the service. When the caller sends a token that
/// passes the administrator policy (the same one the admin uses), unpublished posts and pages are included and flagged as drafts, as
/// BlogEngine.NET shows its administrators the drafts. A visitor, or a person without that right, gets the published content only.
/// </summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
public sealed class PublicController(IPublicContent content, IRightsAuthorizer rights) : ControllerBase
{
    /// <summary>True only for a signed-in administrator: the one condition under which drafts are returned.</summary>
    private async Task<bool> SeesDraftsAsync() =>
        User.Identity?.IsAuthenticated == true && await rights.IsAllowedAsync(User, Rights.PostsRead);

    /// <summary>Published posts, newest first (an administrator also sees drafts); optionally those in one category or carrying one tag.</summary>
    [HttpGet("posts")]
    public async Task<ActionResult<PublicPostList>> Posts(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? category = null, [FromQuery] string? tag = null, CancellationToken cancellationToken = default) =>
        Ok(await content.ListPostsAsync(page, pageSize, string.IsNullOrWhiteSpace(category) ? null : category, string.IsNullOrWhiteSpace(tag) ? null : tag, cancellationToken, await SeesDraftsAsync()));

    [HttpGet("posts/{slug}")]
    public async Task<ActionResult<PublicPost>> Post(string slug, CancellationToken cancellationToken) =>
        await content.GetPostAsync(slug, cancellationToken, await SeesDraftsAsync()) is { } post ? Ok(post) : NotFound();

    [HttpGet("pages/{slug}")]
    public async Task<ActionResult<PublicPage>> Page(string slug, CancellationToken cancellationToken) =>
        await content.GetPageAsync(slug, cancellationToken, await SeesDraftsAsync()) is { } page ? Ok(page) : NotFound();

    /// <summary>The page marked as the front page; 404 when there is none (the site then shows the post list). A draft page is never the front page.</summary>
    [HttpGet("home")]
    public async Task<ActionResult<PublicPage>> Home(CancellationToken cancellationToken) =>
        await content.GetFrontPageAsync(cancellationToken) is { } page ? Ok(page) : NotFound();

    [HttpGet("menu")]
    public async Task<ActionResult<IReadOnlyList<PublicMenuItem>>> Menu(CancellationToken cancellationToken) =>
        Ok(await content.GetMenuAsync(cancellationToken, await SeesDraftsAsync()));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<PublicTerm>>> Categories(CancellationToken cancellationToken) =>
        Ok(await content.GetCategoriesAsync(cancellationToken, await SeesDraftsAsync()));

    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<PublicTerm>>> Tags(CancellationToken cancellationToken) =>
        Ok(await content.GetTagsAsync(cancellationToken, await SeesDraftsAsync()));
}
