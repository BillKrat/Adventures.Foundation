namespace Adventures.Entities;

/// <summary>
/// A post as a list entry. Only what a reader may see: no ids, no e-mail, no internal fields. <paramref name="IsDraft"/> is true only
/// in the view an administrator gets (see <see cref="IPublicContent"/>): a visitor never receives a draft.
/// </summary>
public sealed record PublicPostSummary(
    string Title, string Slug, string? Description, string? DatePublished, string? Author, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags, bool IsDraft = false, string? Approval = null);

/// <summary>A post to read: the summary plus its Markdown content (the reader renders it).</summary>
public sealed record PublicPost(PublicPostSummary Summary, string Content, bool EnableComments);

/// <summary>A page to read, with its parent so the reader can show a breadcrumb; <paramref name="IsDraft"/> as for <see cref="PublicPostSummary"/>.</summary>
public sealed record PublicPage(string Title, string Slug, string? Description, string Content, string? DatePublished, string? ParentTitle, string? ParentSlug, bool IsDraft = false);

/// <summary>One entry of the site menu: a page that is shown in the list, with its sub-pages, in sort order.</summary>
public sealed record PublicMenuItem(string Title, string Slug, IReadOnlyList<PublicMenuItem> Children, bool IsDraft = false);

/// <summary>One page of the post list (newest first), with how many posts match in all.</summary>
public sealed record PublicPostList(IReadOnlyList<PublicPostSummary> Posts, int Page, int PageSize, int Total);

/// <summary>A category or tag with how many posts carry it.</summary>
public sealed record PublicTerm(string Name, int Posts);

/// <summary>
/// What a reader may see: published posts and pages, found by slug, listed newest first, filtered by category or tag, with the site
/// menu built from the page tree and the front page when one is set. Read-only by design: nothing here returns an author's e-mail or
/// any id. A visitor gets published content only. When the host passes <c>includeDrafts</c> (it does so only for a signed-in
/// administrator, as BlogEngine.NET shows its administrators the drafts), unpublished posts and pages are included and carry
/// <c>IsDraft</c>, so the reader can mark them; proposed revisions of published posts are never listed, and a draft page never becomes the front page.
/// </summary>
public interface IPublicContent
{
    Task<PublicPostList> ListPostsAsync(int page, int pageSize, string? category, string? tag, CancellationToken cancellationToken = default, bool includeDrafts = false);

    Task<PublicPost?> GetPostAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false);

    Task<PublicPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false);

    /// <summary>The published page marked as the front page, or null (the site then shows the post list).</summary>
    Task<PublicPage?> GetFrontPageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default, bool includeDrafts = false);

    Task<IReadOnlyList<PublicTerm>> GetCategoriesAsync(CancellationToken cancellationToken = default, bool includeDrafts = false);

    Task<IReadOnlyList<PublicTerm>> GetTagsAsync(CancellationToken cancellationToken = default, bool includeDrafts = false);
}

/// <summary>Implementation of <see cref="IPublicContent"/> over the ordinary presenters (so display names resolve the same way as in the admin), filtered to what is published (or, for an administrator, also the drafts).</summary>
public sealed class PublicContent(IEntityPresenter<Post> posts, IEntityPresenter<Page> pages) : IPublicContent
{
    public const int MaxPageSize = 50;

    public async Task<PublicPostList> ListPostsAsync(int page, int pageSize, string? category, string? tag, CancellationToken cancellationToken = default, bool includeDrafts = false)
    {
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var matching = (await VisiblePostsAsync(includeDrafts, cancellationToken).ConfigureAwait(false))
            .Where(post => category is null || post.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            .Where(post => tag is null || post.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(matching.Count / (double)size));
        var current = Math.Clamp(page, 1, totalPages);
        return new PublicPostList(matching.Skip((current - 1) * size).Take(size).Select(p => p.Summary).ToList(), current, size, matching.Count);
    }

    public async Task<PublicPost?> GetPostAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false)
    {
        var found = (await VisiblePostsAsync(includeDrafts, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(post => string.Equals(post.Summary.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return found is null ? null : new PublicPost(found.Summary, found.Content, found.EnableComments);
    }

    public async Task<PublicPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default, bool includeDrafts = false)
    {
        var all = await VisiblePagesAsync(includeDrafts, cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(row => string.Equals(row.Page.Slug, slug, StringComparison.OrdinalIgnoreCase))?.Page;
    }

    public async Task<PublicPage?> GetFrontPageAsync(CancellationToken cancellationToken = default) =>
        (await VisiblePagesAsync(includeDrafts: false, cancellationToken).ConfigureAwait(false)).FirstOrDefault(page => page.IsFront)?.Page;

    public async Task<IReadOnlyList<PublicMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default, bool includeDrafts = false)
    {
        var listed = (await VisiblePagesAsync(includeDrafts, cancellationToken).ConfigureAwait(false)).Where(page => page.ShowInList).ToList();
        var byParent = listed.ToLookup(page => page.ParentId ?? string.Empty, StringComparer.Ordinal);
        var ids = listed.Select(page => page.Id).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<PublicMenuItem> Build(string parentId, int depth) =>
            depth > 5
                ? []
                : byParent[parentId]
                    .OrderBy(page => page.SortOrder).ThenBy(page => page.Page.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(page => new PublicMenuItem(page.Page.Title, page.Page.Slug, Build(page.Id, depth + 1), page.Page.IsDraft))
                    .ToList();

        // a listed page whose parent is not listed (or not published) still shows, at the top level
        var topLevel = listed.Where(page => page.ParentId is null || !ids.Contains(page.ParentId)).OrderBy(page => page.SortOrder).ThenBy(page => page.Page.Title, StringComparer.OrdinalIgnoreCase);
        return topLevel.Select(page => new PublicMenuItem(page.Page.Title, page.Page.Slug, Build(page.Id, 1), page.Page.IsDraft)).ToList();
    }

    public async Task<IReadOnlyList<PublicTerm>> GetCategoriesAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) =>
        Count((await VisiblePostsAsync(includeDrafts, cancellationToken).ConfigureAwait(false)).SelectMany(post => post.Categories));

    public async Task<IReadOnlyList<PublicTerm>> GetTagsAsync(CancellationToken cancellationToken = default, bool includeDrafts = false) =>
        Count((await VisiblePostsAsync(includeDrafts, cancellationToken).ConfigureAwait(false)).SelectMany(post => post.Tags));

    private static IReadOnlyList<PublicTerm> Count(IEnumerable<string> names) =>
        names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PublicTerm(group.First(), group.Count()))
            .OrderByDescending(term => term.Posts).ThenBy(term => term.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed record PostRow(PublicPostSummary Summary, string Content, bool EnableComments, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags);

    private sealed record PageRow(string Id, PublicPage Page, string? ParentId, int SortOrder, bool ShowInList, bool IsFront);

    private async Task<IReadOnlyList<PostRow>> VisiblePostsAsync(bool includeDrafts, CancellationToken cancellationToken)
    {
        var rows = await AllRowsAsync(posts, "DatePublished", descending: true, cancellationToken).ConfigureAwait(false);
        return rows
            // a proposed revision is a hidden copy of a published post, never a post of its own
            .Where(row => string.IsNullOrEmpty(row.Values.GetValueOrDefault("RevisionOf")))
            .Where(row => includeDrafts || IsTrue(row.Values.GetValueOrDefault("IsPublished")))
            .Select(row =>
            {
                var categories = Names(row, "Categories");
                var tags = Names(row, "Tags");
                var summary = new PublicPostSummary(
                    row.Values.GetValueOrDefault("Title") ?? string.Empty,
                    row.Values.GetValueOrDefault("Slug") ?? string.Empty,
                    row.Values.GetValueOrDefault("Description"),
                    row.Values.GetValueOrDefault("DatePublished"),
                    row.Display?.GetValueOrDefault("Author"),
                    categories,
                    tags,
                    !IsTrue(row.Values.GetValueOrDefault("IsPublished")),
                    // the review state is for the administrator's view only: a visitor never learns that a post is under review
                    includeDrafts && row.Values.GetValueOrDefault("Approval") is { Length: > 0 } approval ? approval : null);
                return new PostRow(summary, row.Values.GetValueOrDefault("Content") ?? string.Empty, IsTrue(row.Values.GetValueOrDefault("EnableComments")), categories, tags);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<PageRow>> VisiblePagesAsync(bool includeDrafts, CancellationToken cancellationToken)
    {
        var rows = await AllRowsAsync(pages, "SortOrder", descending: false, cancellationToken).ConfigureAwait(false);
        var visible = rows.Where(row => includeDrafts || IsTrue(row.Values.GetValueOrDefault("IsPublished"))).ToList();
        var byId = visible.ToDictionary(row => row.EntityId, StringComparer.Ordinal);
        return visible.Select(row =>
        {
            var parentId = row.Values.GetValueOrDefault("Parent");
            var parent = parentId is not null && byId.TryGetValue(parentId, out var p) ? p : null;
            var isDraft = !IsTrue(row.Values.GetValueOrDefault("IsPublished"));
            var page = new PublicPage(
                row.Values.GetValueOrDefault("Title") ?? string.Empty,
                row.Values.GetValueOrDefault("Slug") ?? string.Empty,
                row.Values.GetValueOrDefault("Description"),
                row.Values.GetValueOrDefault("Content") ?? string.Empty,
                row.Values.GetValueOrDefault("DatePublished"),
                parent?.Values.GetValueOrDefault("Title"),
                parent?.Values.GetValueOrDefault("Slug"),
                isDraft);
            _ = int.TryParse(row.Values.GetValueOrDefault("SortOrder"), out var order);
            // a draft page never becomes the front page, even for an administrator
            return new PageRow(row.EntityId, page, parent?.EntityId, order, IsTrue(row.Values.GetValueOrDefault("ShowInList")), !isDraft && IsTrue(row.Values.GetValueOrDefault("IsFrontPage")));
        }).ToList();
    }

    private static async Task<IReadOnlyList<EntityDataModel>> AllRowsAsync<T>(
        IEntityPresenter<T> presenter, string sort, bool descending, CancellationToken cancellationToken)
        where T : DynamicEntity
    {
        var all = new List<EntityDataModel>();
        for (var page = 1; ; page++)
        {
            var grid = await presenter.GetGridAsync(new EntityGridQuery(page, EntityGridQuery.MaxPageSize, sort, descending), cancellationToken).ConfigureAwait(false);
            all.AddRange(grid.Rows);
            if (grid.Paging is null || page >= grid.Paging.TotalPages)
            {
                return all;
            }
        }
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Names(EntityDataModel row, string field) =>
        row.Display?.GetValueOrDefault(field) is { Length: > 0 } text ? text.Split(", ", StringSplitOptions.RemoveEmptyEntries) : [];
}
