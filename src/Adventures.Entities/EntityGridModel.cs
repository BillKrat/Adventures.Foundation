namespace Adventures.Entities;

/// <summary>What a client asks of a grid: which page, how big, and the order. Bound from the query string (<c>?page=2&amp;pageSize=25&amp;sort=UserName&amp;descending=true</c>).</summary>
public sealed record EntityGridQuery(int Page = 1, int PageSize = EntityGridQuery.DefaultPageSize, string? Sort = null, bool Descending = false)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>The query with its numbers forced into range (page at least 1, page size 1 to <see cref="MaxPageSize"/>) so a hostile or sloppy caller cannot ask for everything.</summary>
    public EntityGridQuery Normalized() =>
        this with { Page = Math.Max(1, Page), PageSize = Math.Clamp(PageSize, 1, MaxPageSize), Sort = string.IsNullOrWhiteSpace(Sort) ? null : Sort.Trim() };
}

/// <summary>Where a grid page sits in the whole list, so the renderer can draw the pager without counting anything itself.</summary>
public sealed record GridPaging(int Page, int PageSize, int TotalCount, string? Sort, bool Descending)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// Server-supplied URLs for a grid (relative to the API root), so the client follows links and holds no URL templates.
/// Filled by the host (it knows the routes), not by the presenter. A link that does not apply is null.
/// </summary>
public sealed record EntityGridLinks(string? Self, string? Create = null, string? Next = null, string? Previous = null);

/// <summary>
/// A list of entities for a grid: the columns (the schema, each field with its label, type and whether it is a default
/// column), one <see cref="EntityDataModel"/> per row, the paging state, what the caller may do (<see cref="CanCreate"/>,
/// <see cref="RowPermissions"/>) and the links to follow. A grid built without a query (the exception log, which shows its
/// whole list) has no <see cref="Paging"/>.
/// </summary>
public sealed record EntityGridModel(
    EntitySchemaModel Schema,
    IReadOnlyList<EntityDataModel> Rows,
    GridPaging? Paging = null,
    bool CanCreate = false,
    EntityPermissions? RowPermissions = null,
    EntityGridLinks? Links = null,
    int ContractVersion = EntityContract.Version)
{
    public static EntityGridModel From(
        EntitySchema schema,
        IEnumerable<DynamicEntity> entities,
        IReadOnlySet<string>? readOnlyFields = null,
        IReadOnlySet<string>? hiddenColumns = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entities);

        var rows = entities.Select(entity => EntityFormModel.From(schema, entity).Entity).ToArray();
        return new EntityGridModel(EntitySchemaModel.From(schema, readOnlyFields, hiddenColumns), rows);
    }

    /// <summary>The same grid with the links the host knows how to build.</summary>
    public EntityGridModel WithLinks(EntityGridLinks links) => this with { Links = links };
}

/// <summary>Sorts and pages entities for a grid. In memory over the list the Bll returns (the store has no paged query yet); the payload the client receives is bounded either way.</summary>
public static class EntityGridPager
{
    /// <summary>Applies the (normalized) sort and page; returns the page's entities and the paging state. An unknown sort field is ignored (the list keeps its natural order).</summary>
    public static (IReadOnlyList<TEntity> Page, GridPaging Paging) Apply<TEntity>(
        EntitySchema schema, IReadOnlyList<TEntity> all, EntityGridQuery query)
        where TEntity : DynamicEntity
    {
        var q = query.Normalized();
        var sortField = q.Sort is null ? null : schema.Fields.Values.FirstOrDefault(f => f.Name.Equals(q.Sort, StringComparison.OrdinalIgnoreCase))?.Name;

        IEnumerable<TEntity> ordered = all;
        if (sortField is not null)
        {
            var comparer = Comparer<object?>.Create(CompareValues);
            ordered = q.Descending
                ? all.OrderByDescending(e => e.GetValue(sortField), comparer)
                : all.OrderBy(e => e.GetValue(sortField), comparer);
        }

        var totalPages = all.Count == 0 ? 1 : (int)Math.Ceiling(all.Count / (double)q.PageSize);
        var page = Math.Min(q.Page, totalPages);
        var slice = ordered.Skip((page - 1) * q.PageSize).Take(q.PageSize).ToList();
        return (slice, new GridPaging(page, q.PageSize, all.Count, sortField, sortField is not null && q.Descending));
    }

    /// <summary>Nulls last; strings ignoring case; values of one type by their own order; anything else by its text.</summary>
    private static int CompareValues(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null ? (right is null ? 0 : 1) : -1;
        }

        if (left is string a && right is string b)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        return string.Compare(SchemaTypeConverter.Format(left), SchemaTypeConverter.Format(right), StringComparison.OrdinalIgnoreCase);
    }
}
