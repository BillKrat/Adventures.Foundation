using Adventures.Data.Sql;
using Adventures.Entities;

namespace Adventures.Blog.Legacy;

/// <summary>Pages over <c>be_Pages</c>. <c>IsDeleted</c> is the tombstone, exactly the Dal contract's delete.</summary>
public sealed class LegacyPageDal(ISqlServerExecutor sql, ILegacyBlog blog, EntitySchemaOf<Page> schema)
    : LegacyDalBase<Page>(sql, blog, schema, s => new Page(s)), IEntityDal<Page>
{
    private const string Columns = "PageID, Title, Description, PageContent, Keywords, DateCreated, IsPublished, IsFrontPage, ShowInList, Slug, Parent, SortOrder, IsDeleted";

    public async Task<Page> CreateAsync(Page entity, CancellationToken cancellationToken = default)
    {
        var id = entity.EntityId is not null && TryParseId(entity.EntityId.Value, out var given) ? given : Guid.NewGuid();
        if (await GetAsync(id.ToString(), true, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"A page with id '{id}' already exists.");
        }

        var now = DateTime.UtcNow;
        await Sql.ExecuteAsync(
            "INSERT INTO be_Pages (BlogID, PageID, Title, Description, PageContent, Keywords, DateCreated, DateModified, IsPublished, IsFrontPage, ShowInList, Slug, Parent, SortOrder, IsDeleted) " +
            "VALUES (@BlogId, @Id, @Title, @Description, @Content, @Keywords, @Created, @Now, @IsPublished, @IsFrontPage, @ShowInList, @Slug, @Parent, @SortOrder, 0)",
            await ParametersAsync(entity, id, Date(entity, "DatePublished") ?? now, now, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        entity.Set("Id", Guid.NewGuid().ToString(), id);
        entity.AcceptChanges();
        return entity;
    }

    public async Task<Page?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        if (!TryParseId(entityId, out var id))
        {
            return null;
        }

        var row = await Sql.QuerySingleOrDefaultAsync<PageRow>(
            $"SELECT {Columns} FROM be_Pages WHERE BlogID = @BlogId AND PageID = @Id" + (includeDeleted ? string.Empty : " AND IsDeleted = 0"),
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Id = id }, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<Page>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        (await Sql.QueryAsync<PageRow>(
            $"SELECT {Columns} FROM be_Pages WHERE BlogID = @BlogId" + (includeDeleted ? string.Empty : " AND IsDeleted = 0") + " ORDER BY SortOrder, Title",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false) }, cancellationToken).ConfigureAwait(false))
            .Select(ToEntity).ToList();

    public async Task<int> CountAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        await Sql.QuerySingleOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM be_Pages WHERE BlogID = @BlogId" + (includeDeleted ? string.Empty : " AND IsDeleted = 0"),
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false) }, cancellationToken).ConfigureAwait(false);

    public async Task UpdateAsync(Page entity, CancellationToken cancellationToken = default)
    {
        var changed = await Sql.ExecuteAsync(
            "UPDATE be_Pages SET Title = @Title, Description = @Description, PageContent = @Content, Keywords = @Keywords, DateCreated = @Created, DateModified = @Now, " +
            "IsPublished = @IsPublished, IsFrontPage = @IsFrontPage, ShowInList = @ShowInList, Slug = @Slug, Parent = @Parent, SortOrder = @SortOrder " +
            "WHERE BlogID = @BlogId AND PageID = @Id AND IsDeleted = 0",
            await ParametersAsync(entity, IdOf(entity), Date(entity, "DatePublished") ?? DateTime.UtcNow, DateTime.UtcNow, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        if (changed == 0)
        {
            throw new InvalidOperationException($"Page '{entity.EntityId?.Value}' does not exist or is deleted.");
        }

        entity.AcceptChanges();
    }

    public async Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        TryParseId(entityId, out var id) && await Sql.ExecuteAsync(
            "UPDATE be_Pages SET IsDeleted = 1, DateModified = @Now WHERE BlogID = @BlogId AND PageID = @Id AND IsDeleted = 0",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Id = id, Now = DateTime.UtcNow }, cancellationToken).ConfigureAwait(false) > 0;

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        foreach (var entityId in entityIds)
        {
            deleted += await DeleteAsync(entityId, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        return deleted;
    }

    private async Task<object> ParametersAsync(Page entity, Guid id, DateTime created, DateTime now, CancellationToken cancellationToken) => new
    {
        BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false),
        Id = id,
        Title = Text(entity, "Title") ?? string.Empty,
        Description = Text(entity, "Description"),
        Content = Text(entity, "Content") ?? string.Empty,
        Keywords = Text(entity, "Keywords"),
        Created = created,
        Now = now,
        IsPublished = Flag(entity, "IsPublished"),
        IsFrontPage = Flag(entity, "IsFrontPage"),
        ShowInList = Flag(entity, "ShowInList", true),
        Slug = Text(entity, "Slug") ?? string.Empty,
        Parent = Text(entity, "Parent") is { } parent && Guid.TryParse(parent, out var parentId) ? parentId : Guid.Empty,
        SortOrder = entity.GetValue("SortOrder") is int order ? order : 0,
    };

    private Page ToEntity(PageRow row)
    {
        var entity = NewEntity();
        Put(entity, "Id", row.PageID);
        Put(entity, "Title", row.Title);
        Put(entity, "Description", row.Description);
        Put(entity, "Content", row.PageContent);
        Put(entity, "Keywords", row.Keywords);
        Put(entity, "Slug", row.Slug);
        Put(entity, "DatePublished", row.DateCreated is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null);
        Put(entity, "IsPublished", row.IsPublished);
        Put(entity, "IsFrontPage", row.IsFrontPage);
        Put(entity, "ShowInList", row.ShowInList);
        Put(entity, "SortOrder", row.SortOrder);
        if (row.Parent is { } parent && parent != Guid.Empty)
        {
            Put(entity, "Parent", parent.ToString());
        }

        entity.AcceptChanges();
        return entity;
    }

    /// <summary>The columns read from <c>be_Pages</c> (Dapper binds by name).</summary>
    public sealed class PageRow
    {
        public Guid PageID { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? PageContent { get; set; }

        public string? Keywords { get; set; }

        public DateTime? DateCreated { get; set; }

        public bool IsPublished { get; set; }

        public bool IsFrontPage { get; set; }

        public bool ShowInList { get; set; }

        public string? Slug { get; set; }

        public Guid? Parent { get; set; }

        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
    }
}
