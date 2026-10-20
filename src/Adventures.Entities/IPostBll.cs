namespace Adventures.Entities;

/// <summary>Business layer of posts: the generic rules plus the publish state, which only the server changes (see <see cref="PostBll"/>).</summary>
public interface IPostBll : IEntityBll<Post>
{
    /// <summary>Makes the post public (and stamps the publication date when it has none). False when the post does not exist.</summary>
    Task<bool> PublishAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Takes the post back to a draft. False when the post does not exist.</summary>
    Task<bool> UnpublishAsync(string postId, CancellationToken cancellationToken = default);
}
