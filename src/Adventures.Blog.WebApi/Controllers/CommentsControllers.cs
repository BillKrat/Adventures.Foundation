using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Adventures.Blog;

namespace Adventures.Blog.WebApi;

/// <summary>What a visitor posts. <paramref name="Website"/> is a hidden field no person fills in: a robot that does is dropped without a word.</summary>
public sealed record CommentRequest(string? Author, string? Email, string? Body, string? Website);

public sealed record CommentAccepted(string Message);

/// <summary>
/// The reader side of comments, anonymous: the approved comments under a post, and a form that stores a new one as Pending (rate limited per
/// client address, size capped, the status never read from the request). Nothing here returns an id or an e-mail address.
/// </summary>
[ApiController]
[Route("api/public/posts/{slug}/comments")]
[AllowAnonymous]
public sealed class PublicCommentsController(IVisitorComments comments) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicComment>>> Approved(string slug, CancellationToken cancellationToken) =>
        Ok(await comments.ListApprovedAsync(slug, cancellationToken));

    [HttpPost]
    [EnableRateLimiting(RequestHardening.CommentPolicy)]
    [RequestSizeLimit(16 * 1024)]
    public Task<IActionResult> Submit(string slug, [FromBody] CommentRequest request, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                // the honeypot: answer exactly as for a real comment so a robot learns nothing, and store nothing
                return Accepted(new CommentAccepted(AcceptedMessage));
            }

            await comments.SubmitAsync(new SubmittedComment(slug, request.Author ?? string.Empty, request.Email, request.Body ?? string.Empty), cancellationToken);
            return Accepted(new CommentAccepted(AcceptedMessage));
        });

    private const string AcceptedMessage = "Thank you. Your comment will appear after it has been approved.";
}

public sealed record CommentCount(int Pending);

public sealed record CommentAction(string? Action, IReadOnlyList<string>? Ids);

public sealed record CommentActionResult(int Changed);

/// <summary>Moderation: the list (filter Pending or Approved), the pending count for the menu badge, and approve, unapprove or delete one or many. Needs <see cref="Rights.CommentsModerate"/>.</summary>
[ApiController]
[Route("api/comments")]
[RequireRight(Rights.CommentsModerate)]
public sealed class CommentModerationController(IVisitorComments comments) : ApiControllerBase
{
    public const int MaxIdsPerCall = 500;

    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? status, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(status) && !CommentStatuses.All.Contains(status, StringComparer.Ordinal))
            {
                return BadRequest($"The status must be one of: {string.Join(", ", CommentStatuses.All)}.");
            }

            return Ok(await comments.ListAsync(string.IsNullOrWhiteSpace(status) ? null : status, cancellationToken));
        });

    [HttpGet("count")]
    public async Task<ActionResult<CommentCount>> Count(CancellationToken cancellationToken) =>
        Ok(new CommentCount(await comments.CountPendingAsync(cancellationToken)));

    [HttpPost("process")]
    public Task<IActionResult> Process([FromBody] CommentAction request, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var ids = request.Ids ?? [];
            if (ids.Count == 0 || ids.Count > MaxIdsPerCall)
            {
                return BadRequest($"Send between 1 and {MaxIdsPerCall} comment ids.");
            }

            var changed = request.Action switch
            {
                "approve" => await comments.SetStatusAsync(ids, CommentStatuses.Approved, cancellationToken),
                "unapprove" => await comments.SetStatusAsync(ids, CommentStatuses.Pending, cancellationToken),
                "delete" => await comments.DeleteAsync(ids, cancellationToken),
                _ => -1,
            };
            return changed < 0 ? BadRequest("The action must be approve, unapprove or delete.") : Ok(new CommentActionResult(changed));
        });
}
