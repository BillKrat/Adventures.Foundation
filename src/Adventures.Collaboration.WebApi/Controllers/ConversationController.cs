// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Collaboration;

namespace Adventures.Collaboration.WebApi;

/// <summary>The text and kind of a new entry in a post's conversation; the kind defaults to a plain comment.</summary>
public sealed record NewConversationEntry(string? Text, string? Kind);

/// <summary>
/// The conversation under a post for administrators: read it, and add to it as the signed-in user (the author and the time are the
/// server's, never the caller's). Entries are only added; a decision is an entry of kind Decision that the human records.
/// </summary>
[ApiController]
[Route("api/posts/{postId}/comments")]
[Route("api/collaboration/{postId}/comments")]

public sealed class ConversationController(IConversation conversation, IRightsAuthorizer authorizer) : ApiControllerBase
{
    [RequireRight(Rights.CollaborationRead)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationEntry>>> List(string postId, CancellationToken cancellationToken) =>
        Ok(await conversation.ListAsync(postId, cancellationToken));

    [RequireRight(Rights.CollaborationWrite)]
    [HttpPost]
    public async Task<IActionResult> Add(string postId, [FromBody] NewConversationEntry entry, CancellationToken cancellationToken)
    {
        var kind = string.IsNullOrWhiteSpace(entry.Kind) ? CommentKinds.Comment : entry.Kind;

        // a decision is the human's word: recording one needs the review right, not only the right to comment
        if (string.Equals(kind, CommentKinds.Decision, StringComparison.Ordinal) && !await authorizer.IsAllowedAsync(User, Rights.CollaborationReview, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return await GuardedAsync(async () => (IActionResult)StatusCode(
            StatusCodes.Status201Created,
            await conversation.AddAsync(postId, User.Identity?.Name ?? "unknown", kind, entry.Text ?? string.Empty, cancellationToken)));
    }
}

/// <summary>The decisions page for administrators: every entry the human recorded as a decision, across all posts, newest first, each with its post.</summary>
[ApiController]
[Route("api/decisions")]
[RequireRight(Rights.DecisionsRead)]
[RequiresPlugin(CollaborationPlugins.Decisions)]
public sealed class DecisionsController(IConversation conversation) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DecisionEntry>>> List(CancellationToken cancellationToken) =>
        Ok(await conversation.ListDecisionsAsync(cancellationToken));
}
