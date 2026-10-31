using Adventures.Data.Sql;
using Adventures.Entities;

namespace Adventures.Blog.Legacy;

/// <summary>Categories over <c>be_Categories</c>. There is no tombstone column, so a delete removes the row and its post links.</summary>
public sealed class LegacyCategoryDal(ISqlServerExecutor sql, ILegacyBlog blog, EntitySchemaOf<Category> schema)
    : LegacyDalBase<Category>(sql, blog, schema, s => new Category(s)), IEntityDal<Category>, IRelationNameSource
{
    public string SchemaIri => BlogConstants.Content.CategorySchemaIri;

    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(false, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.EntityId!.Value, c => Text(c, "Name") ?? c.EntityId!.Value, StringComparer.Ordinal);

    public async Task<Category> CreateAsync(Category entity, CancellationToken cancellationToken = default)
    {
        var id = entity.EntityId is not null && TryParseId(entity.EntityId.Value, out var given) ? given : Guid.NewGuid();
        if (await GetAsync(id.ToString(), false, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"A category with id '{id}' already exists.");
        }

        await Sql.ExecuteAsync(
            "INSERT INTO be_Categories (BlogID, CategoryID, CategoryName, Description, ParentID) VALUES (@BlogId, @Id, @Name, @Description, NULL)",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Id = id, Name = Text(entity, "Name") ?? string.Empty, Description = Text(entity, "Description") },
            cancellationToken).ConfigureAwait(false);
        entity.Set("Id", Guid.NewGuid().ToString(), id);
        entity.AcceptChanges();
        return entity;
    }

    public async Task<Category?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        if (!TryParseId(entityId, out var id))
        {
            return null;
        }

        var row = await Sql.QuerySingleOrDefaultAsync<CategoryRow>(
            "SELECT CategoryID, CategoryName, Description FROM be_Categories WHERE BlogID = @BlogId AND CategoryID = @Id",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Id = id }, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<Category>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        (await Sql.QueryAsync<CategoryRow>(
            "SELECT CategoryID, CategoryName, Description FROM be_Categories WHERE BlogID = @BlogId ORDER BY CategoryName",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false) }, cancellationToken).ConfigureAwait(false))
            .Select(ToEntity).ToList();

    public async Task<int> CountAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        await Sql.QuerySingleOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM be_Categories WHERE BlogID = @BlogId",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false) }, cancellationToken).ConfigureAwait(false);

    public async Task UpdateAsync(Category entity, CancellationToken cancellationToken = default)
    {
        var changed = await Sql.ExecuteAsync(
            "UPDATE be_Categories SET CategoryName = @Name, Description = @Description WHERE BlogID = @BlogId AND CategoryID = @Id",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Id = IdOf(entity), Name = Text(entity, "Name") ?? string.Empty, Description = Text(entity, "Description") },
            cancellationToken).ConfigureAwait(false);
        if (changed == 0)
        {
            throw new InvalidOperationException($"Category '{entity.EntityId?.Value}' does not exist.");
        }

        entity.AcceptChanges();
    }

    public async Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default)
    {
        if (!TryParseId(entityId, out var id))
        {
            return false;
        }

        var blogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false);
        await Sql.ExecuteAsync("DELETE FROM be_PostCategory WHERE BlogID = @BlogId AND CategoryID = @Id", new { BlogId = blogId, Id = id }, cancellationToken).ConfigureAwait(false);
        return await Sql.ExecuteAsync("DELETE FROM be_Categories WHERE BlogID = @BlogId AND CategoryID = @Id", new { BlogId = blogId, Id = id }, cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        foreach (var entityId in entityIds)
        {
            deleted += await DeleteAsync(entityId, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        return deleted;
    }

    private Category ToEntity(CategoryRow row)
    {
        var entity = NewEntity();
        Put(entity, "Id", row.CategoryID);
        Put(entity, "Name", row.CategoryName);
        Put(entity, "Description", row.Description);
        entity.AcceptChanges();
        return entity;
    }

    /// <summary>The columns read from <c>be_Categories</c> (Dapper binds by name).</summary>
    public sealed class CategoryRow
    {
        public Guid CategoryID { get; set; }

        public string? CategoryName { get; set; }

        public string? Description { get; set; }
    }
}
