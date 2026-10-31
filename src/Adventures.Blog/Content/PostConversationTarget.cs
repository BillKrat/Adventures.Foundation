using Adventures.Entities;
using Adventures.Collaboration;

namespace Adventures.Blog;

/// <summary>The post side of <see cref="IConversationTarget"/>: a conversation can hang under a post.</summary>
public sealed class PostConversationTarget(IPostBll posts) : IConversationTarget
{
    public async Task<ConversationTarget?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        await posts.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } post
            ? new ConversationTarget(post.GetValue("Title") as string ?? string.Empty, post.GetValue("Slug") as string ?? string.Empty, "post")
            : null;
}
