using Adventures.Common.Exceptions;
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>What a reader wrote under a post. Its schema ships in Sql/seed/seed-021-visitor-comments.nq. Not the team conversation (see Adventures.Collaboration).</summary>
public sealed class VisitorComment(EntitySchema schema) : DynamicEntity(schema);

/// <summary>The states of a visitor comment. A new one is always Pending; only a person with the right moves it to Approved, and only Approved ones are public.</summary>
public static class CommentStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";

    public static readonly IReadOnlyList<string> All = [Pending, Approved];
}

/// <summary>A comment as a visitor submits it. There is no status here: the server decides it.</summary>
public sealed record SubmittedComment(string PostSlug, string Author, string? Email, string Body);

/// <summary>An approved comment as every reader sees it: no id, no e-mail address.</summary>
public sealed record PublicComment(string Author, string Created, string Body);

/// <summary>A comment as the moderator sees it.</summary>
public sealed record ModerationComment(string Id, string PostSlug, string PostTitle, string Author, string? Email, string Created, string Status, string Body);

/// <summary>A published post that may take comments, as far as the comments feature needs to know.</summary>
public sealed record CommentablePost(string Title, bool CommentsEnabled);

/// <summary>Finds a published post by its address. A post that is missing or not published is null.</summary>
public interface ICommentablePosts
{
    Task<CommentablePost?> FindAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary><see cref="ICommentablePosts"/> over the public content (published posts only, never drafts).</summary>
public sealed class PublicContentCommentablePosts(IPublicContent content) : ICommentablePosts
{
    public async Task<CommentablePost?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
        await content.GetPostAsync(slug, cancellationToken, includeDrafts: false).ConfigureAwait(false) is { } post
            ? new CommentablePost(post.Summary.Title, post.EnableComments)
            : null;
}

/// <summary>
/// Visitor comments with moderation: anyone may submit one (the host rate-limits the endpoint), it waits as Pending, and only a person
/// with the right approves, unapproves or deletes. The text is stored as plain text and must be shown as plain text.
/// </summary>
public interface IVisitorComments
{
    /// <summary>Stores a new Pending comment. A business-rule refusal for a missing or comment-closed post, an empty or over-long name or text, a malformed address, or too many comments already waiting.</summary>
    Task SubmitAsync(SubmittedComment comment, CancellationToken cancellationToken = default);

    /// <summary>The approved comments under the post, oldest first.</summary>
    Task<IReadOnlyList<PublicComment>> ListApprovedAsync(string postSlug, CancellationToken cancellationToken = default);

    /// <summary>Every comment, newest first, optionally of one status (null = all).</summary>
    Task<IReadOnlyList<ModerationComment>> ListAsync(string? status, CancellationToken cancellationToken = default);

    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves the comments to the status (one of <see cref="CommentStatuses.All"/>); returns how many exist. Refused for an unknown status.</summary>
    Task<int> SetStatusAsync(IReadOnlyCollection<string> ids, string status, CancellationToken cancellationToken = default);

    /// <summary>Removes the comments; returns how many were removed.</summary>
    Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);
}

/// <summary>Implementation over the visitor comment repository and the published posts.</summary>
public sealed class VisitorComments(IEntityBll<VisitorComment> comments, EntitySchemaOf<VisitorComment> schema, ICommentablePosts posts, ISiteSettings settings) : IVisitorComments
{
    public const int MaxBodyLength = 2000;
    public const int MaxAuthorLength = 80;
    public const int MaxEmailLength = 120;

    /// <summary>How many comments may wait for approval at once: an anonymous endpoint must not be able to fill the store.</summary>
    public const int MaxPending = 200;

    public async Task SubmitAsync(SubmittedComment comment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comment);
        if (!(await settings.GetPublicAsync(cancellationToken).ConfigureAwait(false)).CommentsEnabled)
        {
            throw new BusinessRuleException("Comments are closed on this site.");
        }

        var author = Clean(comment.Author);
        var body = Clean(comment.Body);
        var email = Clean(comment.Email ?? string.Empty);
        if (author.Length is 0 or > MaxAuthorLength)
        {
            throw new BusinessRuleException($"The name must be between 1 and {MaxAuthorLength} characters.");
        }

        if (body.Length is 0 or > MaxBodyLength)
        {
            throw new BusinessRuleException($"The comment must be between 1 and {MaxBodyLength} characters.");
        }

        if (email.Length > 0 && (email.Length > MaxEmailLength || !LooksLikeAnAddress(email)))
        {
            throw new BusinessRuleException("The e-mail address does not look right.");
        }

        var post = string.IsNullOrWhiteSpace(comment.PostSlug) ? null : await posts.FindAsync(comment.PostSlug.Trim(), cancellationToken).ConfigureAwait(false);
        if (post is null || !post.CommentsEnabled)
        {
            throw new BusinessRuleException("Comments are not open on this post.");
        }

        if (await CountPendingAsync(cancellationToken).ConfigureAwait(false) >= MaxPending)
        {
            throw new BusinessRuleException("Too many comments are waiting for approval. Please try again later.");
        }

        var entity = new VisitorComment(schema.Schema);
        entity.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        entity.Set("PostSlug", Guid.NewGuid().ToString(), comment.PostSlug.Trim());
        entity.Set("PostTitle", Guid.NewGuid().ToString(), post.Title);
        entity.Set("Author", Guid.NewGuid().ToString(), author);
        if (email.Length > 0)
        {
            entity.Set("Email", Guid.NewGuid().ToString(), email);
        }

        entity.Set("Created", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        entity.Set("Status", Guid.NewGuid().ToString(), CommentStatuses.Pending);
        entity.Set("Body", Guid.NewGuid().ToString(), body);
        await comments.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PublicComment>> ListApprovedAsync(string postSlug, CancellationToken cancellationToken = default) =>
        (await comments.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => Text(c, "Status") == CommentStatuses.Approved && string.Equals(Text(c, "PostSlug"), postSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => Created(c), StringComparer.Ordinal)
            .Select(c => new PublicComment(Text(c, "Author"), Created(c), Text(c, "Body")))
            .ToList();

    public async Task<IReadOnlyList<ModerationComment>> ListAsync(string? status, CancellationToken cancellationToken = default) =>
        (await comments.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => status is null || Text(c, "Status") == status)
            .OrderByDescending(c => Created(c), StringComparer.Ordinal)
            .Select(c => new ModerationComment(
                c.EntityId?.Value ?? string.Empty, Text(c, "PostSlug"), Text(c, "PostTitle"), Text(c, "Author"),
                c.GetValue("Email") as string, Created(c), Text(c, "Status"), Text(c, "Body")))
            .ToList();

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        (await comments.ListAsync(cancellationToken).ConfigureAwait(false)).Count(c => Text(c, "Status") == CommentStatuses.Pending);

    public async Task<int> SetStatusAsync(IReadOnlyCollection<string> ids, string status, CancellationToken cancellationToken = default)
    {
        if (!CommentStatuses.All.Contains(status, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"The status must be one of: {string.Join(", ", CommentStatuses.All)}.");
        }

        var changed = 0;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (await comments.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } comment)
            {
                continue;
            }

            comment.Set("Status", Guid.NewGuid().ToString(), status);
            await comments.UpdateAsync(comment, cancellationToken).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }

    public Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        comments.DeleteManyAsync(ids.Distinct(StringComparer.Ordinal).ToList(), cancellationToken);

    private static string Text(VisitorComment comment, string field) => comment.GetValue(field) as string ?? string.Empty;

    private static string Created(VisitorComment comment) => comment.GetValue("Created") is DateTimeOffset created ? created.ToString("O") : string.Empty;

    /// <summary>Trims, drops control characters other than line breaks and tabs, and trims again: the text is shown as plain text, never as markup.</summary>
    private static string Clean(string value) =>
        new string(value.Where(ch => !char.IsControl(ch) || ch is '\n' or '\r' or '\t').ToArray()).Trim();

    private static bool LooksLikeAnAddress(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && value.IndexOf('.', at) > at + 1 && !value.Contains(' ', StringComparison.Ordinal);
    }
}
