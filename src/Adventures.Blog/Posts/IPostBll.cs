// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>Business layer of posts: the generic rules plus the publish state and revisions, which only the server changes (see <see cref="PostBll"/>).</summary>
public interface IPostBll : IEntityBll<Post>
{
    /// <summary>Makes the post public (and stamps the publication date when it has none). False when the post does not exist.</summary>
    Task<bool> PublishAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Takes the post back to a draft. False when the post does not exist.</summary>
    Task<bool> UnpublishAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the draft <paramref name="revisionId"/> as a proposed revision of the published post <paramref name="originalId"/>. The
    /// draft stays invisible to readers; the original is untouched until an administrator applies the revision.
    /// </summary>
    Task MarkRevisionAsync(string revisionId, string originalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the revision title, content, description, comments setting, categories and tags onto its original (which keeps its
    /// slug, date, author and published state) and removes the revision. False when the revision does not exist; a business-rule
    /// refusal when it is not a revision or its original is gone.
    /// </summary>
    Task<bool> ApplyRevisionAsync(string revisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the review state of a post to one of <see cref="PostApproval.All"/>. An approval records who approved it and when; any other
    /// state clears both. False when the post does not exist; refused for a proposed revision or an unknown state.
    /// </summary>
    Task<bool> SetApprovalAsync(string postId, string status, string? approvedBy, CancellationToken cancellationToken = default);

    /// <summary>Removes a revision without applying it. False when it does not exist; refused when the post is not a revision.</summary>
    Task<bool> DiscardRevisionAsync(string revisionId, CancellationToken cancellationToken = default);
}
