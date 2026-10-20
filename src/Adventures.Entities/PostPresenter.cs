namespace Adventures.Entities;

/// <summary>The presenter of posts: the publish state is shown but never accepted from the form (it changes through the publish and unpublish actions).</summary>
public sealed class PostPresenter(IPostBll bll, EntitySchemaOf<Post> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<Post>(bll, schema.Schema, s => new Post(s), relations)
{
    private static readonly HashSet<string> Managed = new(["IsPublished"], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ServerManagedFields => Managed;

    protected override IReadOnlySet<string> ReadOnlyFields => Managed;
}
