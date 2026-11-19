using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>The presenter of pages: a new page from the admin is an Html (visual editor) page, and its format is shown but never changed (see <see cref="ContentFormats"/>).</summary>
public sealed class PagePresenter(IPageBll bll, EntitySchemaOf<Page> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<Page>(bll, schema.Schema, s => new Page(s), relations)
{
    private static readonly HashSet<string> ReadOnly = new([ContentFormats.FieldName], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ReadOnlyFields => ReadOnly;
    /// <summary>The editor shows the title, then the content, before the other fields (human, 2026-11-19); the rest keep the schema sequence.</summary>
    protected override IReadOnlyList<string> LeadingFields { get; } = ["Title", "Content"];


    protected override EntityDataModel PrepareCreate(EntityDataModel data) => ContentFormats.DefaultForNewForm(data, Schema);
}
