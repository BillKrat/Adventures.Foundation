using System.Collections.Concurrent;
using Adventures.Data.Sql;
using Adventures.Entities;

namespace Adventures.Blog.Legacy;

/// <summary>
/// Tags that were created but are not on any post yet. BlogEngine has no tag table (a tag is a string on <c>be_PostTag</c>), so a new tag
/// exists in memory until a post uses it. One instance per process.
/// </summary>
public sealed class LegacyTagRegistry
{
    private readonly ConcurrentDictionary<Guid, string> _names = new();

    public void Remember(string name) => _names[LegacyIds.ForTagName(name)] = name.Trim();

    public void Forget(Guid id) => _names.TryRemove(id, out _);

    public IReadOnlyDictionary<Guid, string> Names => _names;
}

/// <summary>Tags over <c>be_PostTag</c>: the distinct tag strings of the blog, each with an id derived from its name.</summary>
public sealed class LegacyTagDal(ISqlServerExecutor sql, ILegacyBlog blog, EntitySchemaOf<Tag> schema, LegacyTagRegistry registry)
    : LegacyDalBase<Tag>(sql, blog, schema, s => new Tag(s)), IEntityDal<Tag>, IRelationNameSource
{
    public string SchemaIri => BlogConstants.Content.TagSchemaIri;

    /// <summary>Tag id to tag name: every distinct string in use plus the tags created but not yet used.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> NamesByIdAsync(CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<Guid, string>(registry.Names);
        foreach (var name in await Sql.QueryAsync<string>(
            "SELECT DISTINCT Tag FROM be_PostTag WHERE BlogID = @BlogId AND Tag IS NOT NULL AND LTRIM(RTRIM(Tag)) <> ''",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false) }, cancellationToken).ConfigureAwait(false))
        {
            map[LegacyIds.ForTagName(name)] = name.Trim();
        }

        return map;
    }

    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(CancellationToken cancellationToken = default) =>
        (await NamesByIdAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Key.ToString(), p => p.Value, StringComparer.Ordinal);

    public Task<Tag> CreateAsync(Tag entity, CancellationToken cancellationToken = default)
    {
        var name = Text(entity, "Name")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("A tag needs a name.");
        }

        registry.Remember(name);
        entity.Set("Id", Guid.NewGuid().ToString(), LegacyIds.ForTagName(name));
        entity.AcceptChanges();
        return Task.FromResult(entity);
    }

    public async Task<Tag?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        TryParseId(entityId, out var id) && (await NamesByIdAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(id, out var name) ? ToEntity(id, name) : null;

    public async Task<IReadOnlyList<Tag>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        (await NamesByIdAsync(cancellationToken).ConfigureAwait(false)).OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Select(p => ToEntity(p.Key, p.Value)).ToList();

    public async Task<int> CountAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        (await NamesByIdAsync(cancellationToken).ConfigureAwait(false)).Count;

    public async Task UpdateAsync(Tag entity, CancellationToken cancellationToken = default)
    {
        var names = await NamesByIdAsync(cancellationToken).ConfigureAwait(false);
        var id = IdOf(entity);
        if (!names.TryGetValue(id, out var oldName))
        {
            throw new InvalidOperationException($"Tag '{entity.EntityId?.Value}' does not exist.");
        }

        var newName = Text(entity, "Name")?.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            throw new InvalidOperationException("A tag needs a name.");
        }

        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            await Sql.ExecuteAsync(
                "UPDATE be_PostTag SET Tag = @New WHERE BlogID = @BlogId AND Tag = @Old",
                new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Old = oldName, New = newName }, cancellationToken).ConfigureAwait(false);
            registry.Forget(id);
            registry.Remember(newName);
            entity.Set("Id", Guid.NewGuid().ToString(), LegacyIds.ForTagName(newName));
        }

        entity.AcceptChanges();
    }

    public async Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default)
    {
        if (!TryParseId(entityId, out var id) || !(await NamesByIdAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(id, out var name))
        {
            return false;
        }

        await Sql.ExecuteAsync(
            "DELETE FROM be_PostTag WHERE BlogID = @BlogId AND Tag = @Name",
            new { BlogId = await Blog.BlogIdAsync(cancellationToken).ConfigureAwait(false), Name = name }, cancellationToken).ConfigureAwait(false);
        registry.Forget(id);
        return true;
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

    private Tag ToEntity(Guid id, string name)
    {
        var entity = NewEntity();
        Put(entity, "Id", id);
        Put(entity, "Name", name);
        entity.AcceptChanges();
        return entity;
    }
}
