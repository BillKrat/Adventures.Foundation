using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>The presenter of pages: a new page from the admin is an Html (visual editor) page, and its format is shown but never changed (see <see cref="ContentFormats"/>).</summary>
public sealed class PagePresenter(IPageBll bll, EntitySchemaOf<Page> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<Page>(bll, schema.Schema, s => new Page(s), relations)
{
    private static readonly HashSet<string> ReadOnly = new([ContentFormats.FieldName], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ReadOnlyFields => ReadOnly;

    protected override EntityDataModel PrepareCreate(EntityDataModel data) => ContentFormats.DefaultForNewForm(data, Schema);
}
