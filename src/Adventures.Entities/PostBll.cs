namespace Adventures.Entities;

/// <summary>
/// The post rules: a new post is always a draft (publishing is its own action, run by someone allowed to publish), gets a slug
/// from its title when it has none and a date; <see cref="PublishAsync"/> and <see cref="UnpublishAsync"/> are the only ways the
/// published state changes.
/// </summary>
public sealed class PostBll(IEntityRepository<Post> posts) : EntityBll<Post>(posts), IPostBll
{
    public override Task<Post> CreateAsync(Post entity, CancellationToken cancellationToken = default)
    {
        Prepare(entity);
        entity.Set("IsPublished", Guid.NewGuid().ToString(), false);
        return base.CreateAsync(entity, cancellationToken);
    }

    public override Task UpdateAsync(Post entity, CancellationToken cancellationToken = default)
    {
        Prepare(entity);
        return base.UpdateAsync(entity, cancellationToken);
    }

    public Task<bool> PublishAsync(string postId, CancellationToken cancellationToken = default) => SetPublishedAsync(postId, true, cancellationToken);

    public Task<bool> UnpublishAsync(string postId, CancellationToken cancellationToken = default) => SetPublishedAsync(postId, false, cancellationToken);

    private async Task<bool> SetPublishedAsync(string postId, bool published, CancellationToken cancellationToken)
    {
        var post = await GetAsync(postId, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return false;
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

    private static void Prepare(Post post)
    {
        if (string.IsNullOrWhiteSpace(post.GetValue("Slug") as string) && post.GetValue("Title") is string title && !string.IsNullOrWhiteSpace(title))
        {
            post.Set("Slug", Guid.NewGuid().ToString(), PageBll.Slugify(title));
        }

        if (post.GetValue("DatePublished") is null)
        {
            post.Set("DatePublished", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }
    }
}
