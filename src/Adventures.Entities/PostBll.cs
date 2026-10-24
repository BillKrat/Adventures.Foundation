// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// The post rules: a new post is always a draft (publishing is its own action, run by someone allowed to publish), gets a slug
/// from its title when it has none and a date; <see cref="PublishAsync"/> and <see cref="UnpublishAsync"/> are the only ways the
/// published state changes; a draft can be marked as a revision of a published post and later applied or discarded.
/// </summary>
public sealed class PostBll(IEntityRepository<Post> posts) : EntityBll<Post>(posts), IPostBll
{
    private static readonly string[] CopiedByRevision = ["Title", "Content", "Description", "EnableComments"];

    public override async Task<Post> CreateAsync(Post entity, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(entity, selfId: null, storedSlug: null, cancellationToken).ConfigureAwait(false);
        entity.Set("IsPublished", Guid.NewGuid().ToString(), false);
        return await base.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public override async Task UpdateAsync(Post entity, CancellationToken cancellationToken = default)
    {
        var id = entity.EntityId?.Value;
        var stored = id is null ? null : await GetAsync(id, cancellationToken).ConfigureAwait(false);
        await PrepareAsync(entity, id, stored?.GetValue("Slug") as string, cancellationToken).ConfigureAwait(false);
        await base.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetApprovalAsync(string postId, string status, string? approvedBy, CancellationToken cancellationToken = default)
    {
        if (!PostApproval.All.Contains(status, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"The review state must be one of: {string.Join(", ", PostApproval.All)}.");
        }

        var post = await GetAsync(postId, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(post.GetValue("RevisionOf") as string))
        {
            throw new BusinessRuleException("This is a proposed revision, which is applied or discarded, not reviewed.");
        }

        post.Set("Approval", Guid.NewGuid().ToString(), status);
        if (status == PostApproval.Approved)
        {
            post.Set("ApprovedBy", Guid.NewGuid().ToString(), approvedBy ?? string.Empty);
            post.Set("ApprovedAt", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }
        else
        {
            post.Clear("ApprovedBy");
            post.Clear("ApprovedAt");
        }

        await Repository.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<bool> PublishAsync(string postId, CancellationToken cancellationToken = default) => SetPublishedAsync(postId, true, cancellationToken);

    public Task<bool> UnpublishAsync(string postId, CancellationToken cancellationToken = default) => SetPublishedAsync(postId, false, cancellationToken);

    public async Task MarkRevisionAsync(string revisionId, string originalId, CancellationToken cancellationToken = default)
    {
        var revision = await GetAsync(revisionId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("The revision does not exist.");
        if (revision.GetValue("IsPublished") is true)
        {
            throw new BusinessRuleException("A published post cannot be a revision.");
        }

        var original = await GetAsync(originalId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("The post to revise does not exist.");
        if (original.GetValue("IsPublished") is not true)
        {
            throw new BusinessRuleException("Only a published post needs a revision; edit a draft directly.");
        }

        revision.Set("RevisionOf", Guid.NewGuid().ToString(), originalId);
        await Repository.UpdateAsync(revision, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ApplyRevisionAsync(string revisionId, CancellationToken cancellationToken = default)
    {
        var revision = await GetAsync(revisionId, cancellationToken).ConfigureAwait(false);
        if (revision is null)
        {
            return false;
        }

        var originalId = revision.GetValue("RevisionOf") as string;
        if (string.IsNullOrEmpty(originalId))
        {
            throw new BusinessRuleException("This post is not a revision of another post.");
        }

        var original = await GetAsync(originalId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("The post this revises no longer exists; discard the revision.");

        foreach (var field in CopiedByRevision)
        {
            Copy(revision, original, field);
        }

        CopyMany(revision, original, "Categories");
        CopyMany(revision, original, "Tags");
        await Repository.UpdateAsync(original, cancellationToken).ConfigureAwait(false);
        return await Repository.DeleteAsync(revisionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DiscardRevisionAsync(string revisionId, CancellationToken cancellationToken = default)
    {
        var revision = await GetAsync(revisionId, cancellationToken).ConfigureAwait(false);
        if (revision is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(revision.GetValue("RevisionOf") as string))
        {
            throw new BusinessRuleException("This post is not a revision; delete it from the list instead.");
        }

        return await Repository.DeleteAsync(revisionId, cancellationToken).ConfigureAwait(false);
    }

    private static void Copy(Post from, Post to, string field)
    {
        if (from.GetValue(field) is { } value)
        {
            to.Set(field, Guid.NewGuid().ToString(), value);
        }
        else if (to.GetValue(field) is not null)
        {
            to.Clear(field);
        }
    }

    private static void CopyMany(Post from, Post to, string field)
    {
        var values = from.GetValues(field);
        if (values.Count == 0 && to.GetValues(field).Count == 0)
        {
            return;
        }

        to.Clear(field);
        foreach (var value in values)
        {
            to.Add(field, Guid.NewGuid().ToString(), value);
        }
    }

    private async Task<bool> SetPublishedAsync(string postId, bool published, CancellationToken cancellationToken)
    {
        var post = await GetAsync(postId, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return false;
        }

        if (published && !string.IsNullOrEmpty(post.GetValue("RevisionOf") as string))
        {
            throw new BusinessRuleException("This is a revision of another post: apply or discard it instead of publishing it.");
        }

        if (post.GetValue("IsPublished") is bool current && current == published)
        {
            return true;
        }

        post.Set("IsPublished", Guid.NewGuid().ToString(), published);
        if (published && post.GetValue("DatePublished") is null)
        {
            post.Set("DatePublished", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }

        await Repository.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task PrepareAsync(Post post, string? selfId, string? storedSlug, CancellationToken cancellationToken)
    {
        var title = post.GetValue("Title") as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(post.GetValue("Slug") as string) || !string.IsNullOrWhiteSpace(title))
        {
            // proposed revisions are hidden copies and never clash; a revision itself is saved before it is marked, so its generated slug may gain a suffix, which nobody reads
            var others = (await ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(p => p.EntityId is not null && string.IsNullOrEmpty(p.GetValue("RevisionOf") as string))
                .Select(p => (p.EntityId!.Value, p.GetValue("Slug") as string ?? string.Empty, p.GetValue("Title") as string ?? string.Empty));
            var resolved = SlugRules.Resolve(post.GetValue("Slug") as string, title, selfId, storedSlug, others);
            if (resolved.Length > 0)
            {
                post.Set("Slug", Guid.NewGuid().ToString(), resolved);
            }
        }

        if (post.GetValue("DatePublished") is null)
        {
            post.Set("DatePublished", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }
    }
}

/// <summary>The review states of a collaboration post (see <see cref="IPostBll.SetApprovalAsync"/>). Empty means the post is not under review.</summary>
public static class PostApproval
{
    public const string NeedsReview = "Needs review";
    public const string Approved = "Approved";
    public const string ChangesRequested = "Changes requested";

    public static readonly IReadOnlyList<string> All = [NeedsReview, Approved, ChangesRequested];

    /// <summary>The category whose posts are reviewed (the collaboration channel); posts written by the authoring surface into it start as <see cref="NeedsReview"/>.</summary>
    public const string CollaborationCategory = "Collaboration";
}
