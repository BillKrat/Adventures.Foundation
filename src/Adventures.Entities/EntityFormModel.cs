namespace Adventures.Entities;

/// <summary>The version of the form/grid JSON contract; a client that does not understand a higher number should refuse cleanly.</summary>
public static class EntityContract
{
    public const int Version = 1;
}

/// <summary>
/// One field's shape, with no value - what a form control needs to render itself. Computed on the server (from the schema
/// and the rules of the presenter), never decided by the client: <see cref="Label"/> is the text to show,
/// <see cref="IsReadOnly"/> says the field may not be changed (the server also refuses a change), <see cref="Order"/>
/// positions the control, <see cref="IsListVisible"/> is the default for whether a grid shows it as a column.
/// </summary>
public sealed record EntityFieldSchema(
    string Name,
    string Type,
    bool IsRequired,
    string? Label = null,
    bool IsReadOnly = false,
    int Order = 0,
    bool IsListVisible = true)
{
    /// <summary>Builds the field the client sees from a schema field; <paramref name="position"/> is its place in the schema when no order is declared.</summary>
    public static EntityFieldSchema From(EntitySchemaField field, int position, bool forceReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new EntityFieldSchema(
            field.Name,
            field.Type,
            field.IsRequired,
            string.IsNullOrWhiteSpace(field.Label) ? Humanize(field.Name) : field.Label,
            forceReadOnly || field.IsReadOnly || field.Name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase),
            field.Order ?? position,
            field.IsListVisible);
    }

    /// <summary>"DisplayName" becomes "Display name"; "DOB" stays "DOB".</summary>
    public static string Humanize(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var startsWord = i > 0 && char.IsUpper(c)
                && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
            if (startsWord)
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

/// <summary>A schema's fields, with no entity data - the "Schema" half of an <see cref="EntityFormModel"/>.</summary>
public sealed record EntitySchemaModel(string SchemaIri, IReadOnlyList<EntityFieldSchema> Fields)
{
    /// <summary>
    /// The client-facing schema: every field with its label, read-only flag and order, sorted by order.
    /// <paramref name="readOnlyFields"/> adds the names a presenter makes read-only on top of what the schema declares.
    /// </summary>
    public static EntitySchemaModel From(EntitySchema schema, IReadOnlySet<string>? readOnlyFields = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var fields = schema.Fields.Values
            .Select((field, position) => EntityFieldSchema.From(field, position, readOnlyFields?.Contains(field.Name) == true))
            .OrderBy(field => field.Order)
            .ToArray();
        return new EntitySchemaModel(schema.SchemaIri, fields);
    }
}

/// <summary>One entity's current values by field name - the "Entity" half of an <see cref="EntityFormModel"/>.</summary>
public sealed record EntityDataModel(string EntityId, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// What the caller may do with one entity, for the renderer to decide which buttons to show. The server still enforces
/// every rule; these flags are only for rendering.
/// </summary>
public sealed record EntityPermissions(bool CanUpdate, bool CanDelete)
{
    public static EntityPermissions Full { get; } = new(true, true);

    public static EntityPermissions ReadOnly { get; } = new(false, false);
}

/// <summary>
/// A list of entities for a grid: the columns (the schema) and one <see cref="EntityDataModel"/> per row. Paging, sorting
/// and per-row permissions join this record in the grid-contract stage; the shape here is its first, minimal form.
/// </summary>
public sealed record EntityGridModel(EntitySchemaModel Schema, IReadOnlyList<EntityDataModel> Rows)
{
    public static EntityGridModel From(EntitySchema schema, IEnumerable<DynamicEntity> entities, IReadOnlySet<string>? readOnlyFields = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entities);

        var rows = entities.Select(entity => EntityFormModel.From(schema, entity).Entity).ToArray();
        return new EntityGridModel(EntitySchemaModel.From(schema, readOnlyFields), rows);
    }
}

/// <summary>
/// The "standard object" a UI form control renders from: a schema-driven entity screen shape and
/// current data, bundled together but kept separate, plus what the caller may do (<see cref="Permissions"/>) and the
/// contract version. Deliberately not <see cref="EntitySchema"/> or
/// <see cref="DynamicEntity"/> themselves - those are DAL/domain-shaped (a <see cref="DynamicEntity"/>
/// is a <see cref="System.Dynamic.DynamicObject"/> and does not serialize cleanly), and not a DataSet -
/// too heavy and table-centric for a single entity form. Built by <see cref="From"/> from any
/// <see cref="EntitySchema"/>/<see cref="DynamicEntity"/> pair, so it works for any entity type, not
/// just <see cref="User"/>.
/// </summary>
public sealed record EntityFormModel(
    EntitySchemaModel Schema,
    EntityDataModel Entity,
    EntityPermissions? Permissions = null,
    int ContractVersion = EntityContract.Version)
{
    public static EntityFormModel From(
        EntitySchema schema, DynamicEntity entity, EntityPermissions? permissions = null, IReadOnlySet<string>? readOnlyFields = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entity);

        var values = schema.Fields.Values.ToDictionary(
            field => field.Name,
            field => SchemaTypeConverter.Format(entity.GetValue(field.Name)),
            StringComparer.OrdinalIgnoreCase);

        var entityId = entity.EntityId?.Value ?? throw new InvalidOperationException("Entity has no id.");

        return new EntityFormModel(
            EntitySchemaModel.From(schema, readOnlyFields),
            new EntityDataModel(entityId, values),
            permissions ?? EntityPermissions.Full);
    }
}
