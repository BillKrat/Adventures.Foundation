using Adventures.Common.Exceptions;
using Adventures.Data.Sql;
using Adventures.Entities;

namespace Adventures.Blog.Legacy;

/// <summary>
/// Posts over <c>be_Posts</c> with their categories (<c>be_PostCategory</c>) and tags (<c>be_PostTag</c>). <c>IsDeleted</c> is the tombstone.
/// BlogEngine has no review state or revision, so a revision (a post carrying <c>RevisionOf</c>) is refused rather than stored as a post of its
/// own. The relation tables are rewritten after the post row; there is no transaction across the statements (the executor has none), so a failure
/// between them leaves the post saved with its previous taxonomy.
/// </summary>
public sealed class LegacyPostDal(ISqlServerExecutor sql, ILegacyBlog blog, EntitySchemaOf<Post> schema, LegacyTagDal tags)
    : LegacyDalBase<Post>(sql, blog, schema, s => new Post(s)), IEntityDal<Post>
{
    private const string Columns = "PostID, Title, Description, PostContent, DateCreated, Author, IsPublished, IsCommentEnabled, Slug, IsDeleted";

    public async Task<Post> CreateAsync(Post entity, CancellationToken cancellationToken = default)
    {
        RefuseRevision(entity);
        var id = entity.EntityId is not null && TryParseId(entity.EntityId.Value, out var given) ? given : Guid.NewGuid();
        if (await GetAsync(id.ToString(), true, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"A post with id '{id}' already exists.");
        }

        var now = DateTime.UtcNow;
        var blogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false);
        await Sql.ExecuteAsync(
            "INSERT INTO be_Posts (BlogID, PostID, Title, Description, PostContent, DateCreated, DateModified, Author, IsPublished, IsCommentEnabled, Raters, Rating, Slug, IsDeleted) " +
            "VALUES (@BlogId, @Id, @Title, @Description, @Content, @Created, @Now, @Author, @IsPublished, @IsCommentEnabled, 0, 0, @Slug, 0)",
            Parameters(entity, blogId, id, Date(entity, "DatePublished") ?? now, now), cancellationToken).ConfigureAwait(false);
        await WriteTaxonomyAsync(entity, blogId, id, cancellationToken).ConfigureAwait(false);
        entity.Set("Id", Guid.NewGuid().ToString(), id);
        entity.AcceptChanges();
        return entity;
    }

    public async Task<Post?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        if (!TryParseId(entityId, out var id))
        {
            return null;
        }

        var blogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false);
        var row = await Sql.QuerySingleOrDefaultAsync<PostRow>(
            $"SELECT {Columns} FROM be_Posts WHERE BlogID = @BlogId AND PostID = @Id" + (includeDeleted ? string.Empty : " AND IsDeleted = 0"),
            new { BlogId = blogId, Id = id }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var categories = await Sql.QueryAsync<Guid>(
            "SELECT CategoryID FROM be_PostCategory WHERE BlogID = @BlogId AND PostID = @Id", new { BlogId = blogId, Id = id }, cancellationToken).ConfigureAwait(false);
        var tagNames = await Sql.QueryAsync<string>(
            "SELECT Tag FROM be_PostTag WHERE BlogID = @BlogId AND PostID = @Id", new { BlogId = blogId, Id = id }, cancellationToken).ConfigureAwait(false);
        return ToEntity(row, categories, tagNames);
    }

    public async Task<IReadOnlyList<Post>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var blogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Sql.QueryAsync<PostRow>(
            $"SELECT {Columns} FROM be_Posts WHERE BlogID = @BlogId" + (includeDeleted ? string.Empty : " AND IsDeleted = 0") + " ORDER BY DateCreated DESC",
            new { BlogId = blogId }, cancellationToken).ConfigureAwait(false);
        var categoryLinks = (await Sql.QueryAsync<CategoryLink>(
            "SELECT PostID, CategoryID FROM be_PostCategory WHERE BlogID = @BlogId", new { BlogId = blogId }, cancellationToken).ConfigureAwait(false))
            .ToLookup(l => l.PostID, l => l.CategoryID);
        var tagLinks = (await Sql.QueryAsync<TagLink>(
            "SELECT PostID, Tag FROM be_PostTag WHERE BlogID = @BlogId", new { BlogId = blogId }, cancellationToken).ConfigureAwait(false))
            .ToLookup(l => l.PostID, l => l.Tag ?? string.Empty);
        return rows.Select(row => ToEntity(row, categoryLinks[row.PostID], tagLinks[row.PostID])).ToList();
    }

    public async Task<int> CountAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        await Sql.QuerySingleOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM be_Posts WHERE BlogID = @BlogId" + (includeDeleted ? string.Empty : " AND IsDeleted = 0"),
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false) }, cancellationToken).ConfigureAwait(false);

    public async Task UpdateAsync(Post entity, CancellationToken cancellationToken = default)
    {
        RefuseRevision(entity);
        var blogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false);
        var id = IdOf(entity);
        var changed = await Sql.ExecuteAsync(
            "UPDATE be_Posts SET Title = @Title, Description = @Description, PostContent = @Content, DateCreated = @Created, DateModified = @Now, Author = @Author, " +
            "IsPublished = @IsPublished, IsCommentEnabled = @IsCommentEnabled, Slug = @Slug WHERE BlogID = @BlogId AND PostID = @Id AND IsDeleted = 0",
            Parameters(entity, blogId, id, Date(entity, "DatePublished") ?? DateTime.UtcNow, DateTime.UtcNow), cancellationToken).ConfigureAwait(false);
        if (changed == 0)
        {
            throw new InvalidOperationException($"Post '{entity.EntityId?.Value}' does not exist or is deleted.");
        }

        await WriteTaxonomyAsync(entity, blogId, id, cancellationToken).ConfigureAwait(false);
        entity.AcceptChanges();
    }

    public async Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        TryParseId(entityId, out var id) && await Sql.ExecuteAsync(
            "UPDATE be_Posts SET IsDeleted = 1, DateModified = @Now WHERE BlogID = @BlogId AND PostID = @Id AND IsDeleted = 0",
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

    private static void RefuseRevision(Post entity)
    {
        if (!string.IsNullOrEmpty(Text(entity, "RevisionOf")))
        {
            throw new BusinessRuleException("Revisions are not available in Legacy mode (the BlogEngine tables have no place for them).");
        }
    }

    private static object Parameters(Post entity, Guid blogId, Guid id, DateTime created, DateTime now) => new
    {
        BlogId = blogId,
        Id = id,
        Title = Text(entity, "Title") ?? string.Empty,
        Description = Text(entity, "Description"),
        Content = Text(entity, "Content") ?? string.Empty,
        Created = created,
        Now = now,
        Author = Text(entity, "Author"),
        IsPublished = Flag(entity, "IsPublished"),
        IsCommentEnabled = Flag(entity, "EnableComments", true),
        Slug = Text(entity, "Slug") ?? string.Empty,
    };

    /// <summary>Replaces the post's category and tag links with the entity's current values.</summary>
    private async Task WriteTaxonomyAsync(Post entity, Guid blogId, Guid postId, CancellationToken cancellationToken)
    {
        var categoryIds = DistinctTexts(entity.GetValues("Categories")).Where(v => Guid.TryParse(v, out _)).Select(Guid.Parse).Distinct().ToList();
        await Sql.ExecuteAsync("DELETE FROM be_PostCategory WHERE BlogID = @BlogId AND PostID = @PostId", new { BlogId = blogId, PostId = postId }, cancellationToken).ConfigureAwait(false);
        foreach (var categoryId in categoryIds)
        {
            await Sql.ExecuteAsync(
                "INSERT INTO be_PostCategory (BlogID, PostID, CategoryID) VALUES (@BlogId, @PostId, @CategoryId)",
                new { BlogId = blogId, PostId = postId, CategoryId = categoryId }, cancellationToken).ConfigureAwait(false);
        }

        var tagIds = DistinctTexts(entity.GetValues("Tags")).Where(v => Guid.TryParse(v, out _)).Select(Guid.Parse).Distinct().ToList();
        var names = tagIds.Count == 0 ? new Dictionary<Guid, string>() : (await tags.NamesByIdAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Key, p => p.Value);
        await Sql.ExecuteAsync("DELETE FROM be_PostTag WHERE BlogID = @BlogId AND PostID = @PostId", new { BlogId = blogId, PostId = postId }, cancellationToken).ConfigureAwait(false);
        foreach (var tagId in tagIds)
        {
            if (!names.TryGetValue(tagId, out var name))
            {
                throw new BusinessRuleException($"The tag '{tagId}' does not exist.");
            }

            await Sql.ExecuteAsync(
                "INSERT INTO be_PostTag (BlogID, PostID, Tag) VALUES (@BlogId, @PostId, @Tag)",
                new { BlogId = blogId, PostId = postId, Tag = name }, cancellationToken).ConfigureAwait(false);
        }
    }

    private Post ToEntity(PostRow row, IEnumerable<Guid> categoryIds, IEnumerable<string> tagNames)
    {
        var entity = NewEntity();
        Put(entity, "Id", row.PostID);
        Put(entity, "Title", row.Title);
        Put(entity, "Slug", row.Slug);
        Put(entity, "Description", row.Description);
        Put(entity, "Content", row.PostContent);
        Put(entity, "Author", row.Author);
        Put(entity, "DatePublished", row.DateCreated is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null);
        Put(entity, "IsPublished", row.IsPublished);
        Put(entity, "EnableComments", row.IsCommentEnabled);
        PutMany(entity, "Categories", categoryIds.Select(c => c.ToString()).Distinct(StringComparer.Ordinal));
        PutMany(entity, "Tags", tagNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => LegacyIds.ForTagName(n).ToString()).Distinct(StringComparer.Ordinal));
        entity.AcceptChanges();
        return entity;
    }

    /// <summary>The columns read from <c>be_Posts</c> (Dapper binds by name).</summary>
    public sealed class PostRow
    {
        public Guid PostID { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? PostContent { get; set; }

        public DateTime? DateCreated { get; set; }

        public string? Author { get; set; }

        public bool IsPublished { get; set; }

        public bool IsCommentEnabled { get; set; }

        public string? Slug { get; set; }

        public bool IsDeleted { get; set; }
    }

    public sealed class CategoryLink
    {
        public Guid PostID { get; set; }

        public Guid CategoryID { get; set; }
    }

    public sealed class TagLink
    {
        public Guid PostID { get; set; }

        public string? Tag { get; set; }
    }
}
