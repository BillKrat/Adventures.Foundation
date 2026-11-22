using Adventures.Entities;

namespace Adventures.Blog;

public sealed record ExportedPost(
    string Id, string Title, string Slug, string? Description, string Content, string ContentFormat, bool IsPublished, string? DatePublished,
    bool EnableComments, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags);

public sealed record ExportedPage(
    string Id, string Title, string Slug, string? Description, string? Keywords, string Content, string ContentFormat, bool IsPublished,
    string? Parent, int SortOrder, bool ShowInList, bool IsFrontPage);

/// <summary>A plain JSON backup of the site's posts and pages (drafts included, proposed revisions left out). It names no user, password or secret.</summary>
public sealed record ContentExportDocument(string Format, int Version, DateTimeOffset ExportedUtc, IReadOnlyList<ExportedPost> Posts, IReadOnlyList<ExportedPage> Pages);

/// <summary>Builds the export from the rows the editors show; a pure function, so what leaves is tested without a store.</summary>
public static class ContentExport
{
    public const string FormatName = "adventures-content-export";

    public static ContentExportDocument Build(IEnumerable<EntityDataModel> posts, IEnumerable<EntityDataModel> pages, DateTimeOffset now) =>
        new(
            FormatName,
            1,
            now,
            posts.Where(row => string.IsNullOrEmpty(Text(row, "RevisionOf"))).Select(Post).ToList(),
            pages.Select(Page).ToList());

    private static ExportedPost Post(EntityDataModel row) => new(
        row.EntityId, Text(row, "Title") ?? string.Empty, Text(row, "Slug") ?? string.Empty, Text(row, "Description"), Text(row, "Content") ?? string.Empty,
        Text(row, "ContentFormat") ?? "Markdown", Flag(row, "IsPublished"), Text(row, "DatePublished"), Flag(row, "EnableComments"),
        Names(row, "Categories"), Names(row, "Tags"));

    private static ExportedPage Page(EntityDataModel row) => new(
        row.EntityId, Text(row, "Title") ?? string.Empty, Text(row, "Slug") ?? string.Empty, Text(row, "Description"), Text(row, "Keywords"),
        Text(row, "Content") ?? string.Empty, Text(row, "ContentFormat") ?? "Markdown", Flag(row, "IsPublished"),
        row.Display?.GetValueOrDefault("Parent") is { Length: > 0 } parent ? parent : null,
        int.TryParse(Text(row, "SortOrder"), out var order) ? order : 0, Flag(row, "ShowInList"), Flag(row, "IsFrontPage"));

    private static string? Text(EntityDataModel row, string field) => row.Values.GetValueOrDefault(field);

    private static bool Flag(EntityDataModel row, string field) => string.Equals(Text(row, field), "true", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Names(EntityDataModel row, string field) =>
        row.Display?.GetValueOrDefault(field) is { Length: > 0 } text ? text.Split(", ", StringSplitOptions.RemoveEmptyEntries) : [];
}

/// <summary>Reads every post and page of the tenant for <see cref="ContentExport"/>.</summary>
public interface IContentExporter
{
    Task<ContentExportDocument> ExportAsync(CancellationToken cancellationToken = default);
}

public sealed class ContentExporter(IEntityPresenter<Post> posts, IEntityPresenter<Page> pages) : IContentExporter
{
    public async Task<ContentExportDocument> ExportAsync(CancellationToken cancellationToken = default) =>
        ContentExport.Build(await AllAsync(posts, cancellationToken).ConfigureAwait(false), await AllAsync(pages, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

    private static async Task<IReadOnlyList<EntityDataModel>> AllAsync<T>(IEntityPresenter<T> presenter, CancellationToken cancellationToken)
        where T : DynamicEntity
    {
        var all = new List<EntityDataModel>();
        for (var page = 1; ; page++)
        {
            var grid = await presenter.GetGridAsync(new EntityGridQuery(page, EntityGridQuery.MaxPageSize), cancellationToken).ConfigureAwait(false);
            all.AddRange(grid.Rows);
            if (grid.Paging is null || page >= grid.Paging.TotalPages)
            {
                return all;
            }
        }
    }
}
