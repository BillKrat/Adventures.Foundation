// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Blog;

namespace Adventures.Blog.WebApi;

public sealed record TermRequest(string? Name);

/// <summary>
/// The drafts-only authoring surface for Claude (machine identity <c>claude-author</c>, scope <c>content.draft</c>), reached
/// directly or through the MCP server write tools. It can create and revise drafts of its own posts, list them, and list and
/// create categories and tags by name. It has no route to publish a post, to change a published one, or to change anyone
/// else's: those stay administrator actions in the admin (see <see cref="IContentAuthoring"/>). Every post is authored by the
/// configured author account (<c>Authoring:AuthorUserName</c>, default "Claude"), whichever client calls.
/// </summary>
[ApiController]
[Route("api/author")]
[Authorize(Policy = "Scope:" + RequiredScope)]
public sealed class AuthoringController(IContentAuthoring authoring, IConfiguration configuration) : ApiControllerBase
{
    public const string RequiredScope = "content.draft";

    private string Author => configuration["Authoring:AuthorUserName"] ?? "Claude";

    [HttpGet("posts")]
    public async Task<ActionResult<IReadOnlyList<DraftSummary>>> Mine(CancellationToken cancellationToken) =>
        Ok(await authoring.ListMineAsync(Author, cancellationToken));

    [HttpPost("posts")]
    public Task<IActionResult> Create([FromBody] DraftPost draft, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)StatusCode(StatusCodes.Status201Created, await authoring.CreateDraftAsync(Author, draft, cancellationToken)));

    [HttpPut("posts/{id}")]
    public Task<IActionResult> Update(string id, [FromBody] DraftPost draft, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var updated = await authoring.UpdateDraftAsync(Author, id, draft, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        });

    /// <summary>The conversation under one of the author own posts (oldest first).</summary>
    [HttpGet("posts/{id}/comments")]
    public Task<IActionResult> Comments(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)Ok(await authoring.ListCommentsAsync(Author, id, cancellationToken)));

    /// <summary>Adds a comment to the conversation under one of the author own posts (a decision is recorded by the human, never here).</summary>
    [HttpPost("posts/{id}/comments")]
    public Task<IActionResult> AddComment(string id, [FromBody] AuthoringCommentRequest entry, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)StatusCode(StatusCodes.Status201Created, await authoring.AddCommentAsync(Author, id, entry.Text ?? string.Empty, cancellationToken)));

    /// <summary>Proposes changes to one of the author own published posts as a hidden revision draft; an administrator applies or discards it.</summary>
    [HttpPost("posts/{id}/revision")]
    public Task<IActionResult> ProposeRevision(string id, [FromBody] DraftPost changes, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var revision = await authoring.ProposeRevisionAsync(Author, id, changes, cancellationToken);
            return revision is null ? NotFound() : Ok(revision);
        });

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<TaxonomyTerm>>> Categories(CancellationToken cancellationToken) =>
        Ok(await authoring.ListCategoriesAsync(cancellationToken));

    [HttpPost("categories")]
    public Task<IActionResult> EnsureCategory([FromBody] TermRequest request, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? Task.FromResult<IActionResult>(BadRequest("A name is required."))
            : GuardedAsync(async () => Ok(await authoring.EnsureCategoryAsync(request.Name, cancellationToken)));

    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<TaxonomyTerm>>> Tags(CancellationToken cancellationToken) =>
        Ok(await authoring.ListTagsAsync(cancellationToken));

    [HttpPost("tags")]
    public Task<IActionResult> EnsureTag([FromBody] TermRequest request, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? Task.FromResult<IActionResult>(BadRequest("A name is required."))
            : GuardedAsync(async () => Ok(await authoring.EnsureTagAsync(request.Name, cancellationToken)));
}

/// <summary>The body of a comment added through the authoring surface (the same JSON shape as the admin conversation endpoint).</summary>
public sealed record AuthoringCommentRequest(string? Text, string? Kind);
