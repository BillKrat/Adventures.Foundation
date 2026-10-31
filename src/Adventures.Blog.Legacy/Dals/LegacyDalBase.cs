using Adventures.Data.Sql;
using Adventures.Entities;

namespace Adventures.Blog.Legacy;

/// <summary>
/// What every legacy Dal shares: the SQL Server executor, the blog the host serves, and the entity schema. A legacy Dal fills entities from
/// the BlogEngine columns and writes the changed values back with bound parameters; it applies no business rule (the Bll does).
/// </summary>
public abstract class LegacyDalBase<TEntity>(ISqlServerExecutor sql, ILegacyBlog blog, EntitySchemaOf<TEntity> schema, Func<EntitySchema, TEntity> factory)
    where TEntity : DynamicEntity
{
    protected ISqlServerExecutor Sql { get; } = sql;

    protected ILegacyBlog Blog { get; } = blog;

    protected TEntity NewEntity() => factory(schema.Schema);

    /// <summary>Sets a single-valued field; a null value leaves the field empty.</summary>
    protected static void Put(DynamicEntity entity, string field, object? value)
    {
        if (value is not null)
        {
            entity.Set(field, Guid.NewGuid().ToString(), value);
        }
    }

    protected static void PutMany(DynamicEntity entity, string field, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            entity.Add(field, Guid.NewGuid().ToString(), value);
        }
    }

    protected static string? Text(DynamicEntity entity, string field) => entity.GetValue(field) as string;

    protected static bool Flag(DynamicEntity entity, string field, bool fallback = false) => entity.GetValue(field) is bool b ? b : fallback;

    protected static DateTime? Date(DynamicEntity entity, string field) =>
        entity.GetValue(field) switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            DateTime dateTime => dateTime,
            _ => null,
        };

    protected static Guid IdOf(DynamicEntity entity) =>
        entity.EntityId is { } id && Guid.TryParse(id.Value, out var parsed) ? parsed : throw new InvalidOperationException("The entity has no valid id.");

    protected static bool TryParseId(string entityId, out Guid id) => Guid.TryParse(entityId, out id);

    protected static string[] DistinctTexts(IEnumerable<object> values) =>
        values.Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.Ordinal).ToArray();
}
