// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>The presenter of posts: the publish state is shown but never accepted from the form (it changes through the publish and unpublish actions).</summary>
public sealed class PostPresenter(IPostBll bll, EntitySchemaOf<Post> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<Post>(bll, schema.Schema, s => new Post(s), relations)
{
    private static readonly HashSet<string> Managed = new(["IsPublished", "RevisionOf", "Approval", "ApprovedBy", "ApprovedAt"], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ServerManagedFields => Managed;

    protected override IReadOnlySet<string> ReadOnlyFields => Managed;

    /// <summary>Newest first: a draft is dated when it is created, so the posts to review or publish are at the top of the list.</summary>
    protected override (string Field, bool Descending)? DefaultSort => ("DatePublished", true);
}
