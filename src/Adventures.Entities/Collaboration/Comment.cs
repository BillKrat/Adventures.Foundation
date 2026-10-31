// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>One entry of the conversation under a post (see <see cref="IConversation"/>); its schema ships in Sql/seed/seed-014-comments.nq.</summary>
public sealed class Comment(EntitySchema schema) : DynamicEntity(schema);

/// <summary>The kinds of a conversation entry. A <see cref="Decision"/> is what the human recorded as decided; it is what a decisions page will list.</summary>
public static class CommentKinds
{
    public const string Comment = "Comment";
    public const string Decision = "Decision";

    public static readonly IReadOnlyList<string> All = [Comment, Decision];
}

/// <summary>One entry of a post's conversation, as the reader of it sees it.</summary>
public sealed record ConversationEntry(string Id, string Author, string Kind, string Created, string Body);

/// <summary>A decision with the post it was recorded under.</summary>
public sealed record DecisionEntry(string Id, string PostId, string PostTitle, string PostSlug, string Author, string Created, string Body, string Source = "post");

/// <summary>
/// The durable record of the conversation about a post: who said what, when, and which statements were decisions. Entries are only
/// added (never edited or removed here), oldest first, so the thread reads as it happened. The host decides who may call it.
/// </summary>
public interface IConversation
{
    /// <summary>Oldest first. Empty for a post with no entries or one that does not exist.</summary>
    Task<IReadOnlyList<ConversationEntry>> ListAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Adds an entry. Refused (a business-rule error) for a missing post, an unknown kind, an empty text or a text over <see cref="Conversation.MaxBodyLength"/>.</summary>
    Task<ConversationEntry> AddAsync(string postId, string author, string kind, string body, CancellationToken cancellationToken = default);

    /// <summary>Every entry of kind Decision across all posts, newest first, each with the post it belongs to (a post that no longer exists is skipped).</summary>
    Task<IReadOnlyList<DecisionEntry>> ListDecisionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Something a conversation can hang under (a post, a collaboration item). Each feature registers its own, so the conversation knows no feature.</summary>
public interface IConversationTarget
{
    /// <summary>The target with this id, or null when this feature has none.</summary>
    Task<ConversationTarget?> FindAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>What the conversation shows about its target; <paramref name="Source"/> says which feature owns it ("post", "collaboration").</summary>
public sealed record ConversationTarget(string Title, string Slug, string Source);

/// <summary>The collaboration item side of <see cref="IConversationTarget"/>.</summary>
public sealed class CollaborationConversationTarget(ICollaborationBll items) : IConversationTarget
{
    public async Task<ConversationTarget?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        await items.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } item
            ? new ConversationTarget(item.GetValue("Title") as string ?? string.Empty, string.Empty, "collaboration")
            : null;
}

/// <summary>Implementation over the comment repository and the post rules.</summary>
public sealed class Conversation(IEntityBll<Comment> comments, IEnumerable<IConversationTarget> targets, EntitySchemaOf<Comment> schema) : IConversation
{
    public const int MaxBodyLength = 8000;

    public async Task<IReadOnlyList<ConversationEntry>> ListAsync(string postId, CancellationToken cancellationToken = default) =>
        (await comments.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => string.Equals(c.GetValue("Post") as string, postId, StringComparison.Ordinal))
            .Select(ToEntry)
            .OrderBy(entry => entry.Created, StringComparer.Ordinal)
            .ToList();

    public async Task<ConversationEntry> AddAsync(string postId, string author, string kind, string body, CancellationToken cancellationToken = default)
    {
        if (!CommentKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            throw new Adventures.Common.Exceptions.BusinessRuleException($"The kind must be one of: {string.Join(", ", CommentKinds.All)}.");
        }

        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxBodyLength)
        {
            throw new Adventures.Common.Exceptions.BusinessRuleException($"The text must be between 1 and {MaxBodyLength} characters.");
        }

        if (await TargetAsync(postId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new Adventures.Common.Exceptions.BusinessRuleException("The post or collaboration item does not exist.");
        }

        var comment = new Comment(schema.Schema);
        comment.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        comment.Set("Post", Guid.NewGuid().ToString(), postId);
        comment.Set("Author", Guid.NewGuid().ToString(), string.IsNullOrWhiteSpace(author) ? "unknown" : author.Trim());
        comment.Set("Kind", Guid.NewGuid().ToString(), kind);
        comment.Set("Created", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        comment.Set("Body", Guid.NewGuid().ToString(), text);
        return ToEntry(await comments.CreateAsync(comment, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<DecisionEntry>> ListDecisionsAsync(CancellationToken cancellationToken = default)
    {
        var decisions = (await comments.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => string.Equals(c.GetValue("Kind") as string, CommentKinds.Decision, StringComparison.Ordinal))
            .ToList();
        var result = new List<DecisionEntry>();
        foreach (var group in decisions.GroupBy(c => c.GetValue("Post") as string ?? string.Empty))
        {
            var target = group.Key.Length == 0 ? null : await TargetAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                continue;
            }

            result.AddRange(group.Select(ToEntry).Select(e => new DecisionEntry(
                e.Id, group.Key, target.Value.Title, target.Value.Slug, e.Author, e.Created, e.Body, target.Value.Source)));
        }

        return result.OrderByDescending(d => d.Created, StringComparer.Ordinal).ToList();
    }

    /// <summary>What a conversation hangs under: the first registered target that knows the id (a post, a collaboration item). Null when none does.</summary>
    private async Task<(string Title, string Slug, string Source)?> TargetAsync(string id, CancellationToken cancellationToken)
    {
        foreach (var target in targets)
        {
            if (await target.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } found)
            {
                return (found.Title, found.Slug, found.Source);
            }
        }

        return null;
    }

    private static ConversationEntry ToEntry(Comment comment) => new(
        comment.EntityId?.Value ?? string.Empty,
        comment.GetValue("Author") as string ?? string.Empty,
        comment.GetValue("Kind") as string ?? CommentKinds.Comment,
        comment.GetValue("Created") is DateTimeOffset created ? created.ToString("O") : string.Empty,
        comment.GetValue("Body") as string ?? string.Empty);
}
