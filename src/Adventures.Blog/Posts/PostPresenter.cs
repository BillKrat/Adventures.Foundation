// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>The presenter of posts: the publish state is shown but never accepted from the form (it changes through the publish and unpublish actions).</summary>
public sealed class PostPresenter(IPostBll bll, EntitySchemaOf<Post> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<Post>(bll, schema.Schema, s => new Post(s), relations)
{
    private static readonly HashSet<string> Managed = new(["IsPublished", "RevisionOf", "Approval", "ApprovedBy", "ApprovedAt"], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ServerManagedFields => Managed;

    private static readonly HashSet<string> ReadOnly = new(["IsPublished", "RevisionOf", "Approval", "ApprovedBy", "ApprovedAt", ContentFormats.FieldName], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ReadOnlyFields => ReadOnly;
    /// <summary>The editor shows the title, then the content, before the other fields (human, 2026-11-19); the rest keep the schema sequence.</summary>
    protected override IReadOnlyList<string> LeadingFields { get; } = ["Title", "Content"];


    protected override EntityDataModel PrepareCreate(EntityDataModel data) => ContentFormats.DefaultForNewForm(data, Schema);

    /// <summary>Newest first: a draft is dated when it is created, so the posts to review or publish are at the top of the list.</summary>
    protected override (string Field, bool Descending)? DefaultSort => ("DatePublished", true);
}
