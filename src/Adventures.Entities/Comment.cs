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
public sealed record DecisionEntry(string Id, string PostId, string PostTitle, string PostSlug, string Author, string Created, string Body);

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

/// <summary>Implementation over the comment repository and the post rules.</summary>
public sealed class Conversation(IEntityBll<Comment> comments, IPostBll posts, EntitySchemaOf<Comment> schema) : IConversation
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

        if (await posts.GetAsync(postId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new Adventures.Common.Exceptions.BusinessRuleException("The post does not exist.");
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
            var post = group.Key.Length == 0 ? null : await posts.GetAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (post is null)
            {
                continue;
            }

            result.AddRange(group.Select(ToEntry).Select(e => new DecisionEntry(
                e.Id, group.Key, post.GetValue("Title") as string ?? string.Empty, post.GetValue("Slug") as string ?? string.Empty, e.Author, e.Created, e.Body)));
        }

        return result.OrderByDescending(d => d.Created, StringComparer.Ordinal).ToList();
    }

    private static ConversationEntry ToEntry(Comment comment) => new(
        comment.EntityId?.Value ?? string.Empty,
        comment.GetValue("Author") as string ?? string.Empty,
        comment.GetValue("Kind") as string ?? CommentKinds.Comment,
        comment.GetValue("Created") is DateTimeOffset created ? created.ToString("O") : string.Empty,
        comment.GetValue("Body") as string ?? string.Empty);
}
