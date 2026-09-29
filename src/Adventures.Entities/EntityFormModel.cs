namespace Adventures.Entities;

/// <summary>One field's shape, with no value - the part a form control needs to know how to render a control.</summary>
public sealed record EntityFieldSchema(string Name, string Type, bool IsRequired);

/// <summary>A schema's fields, with no entity data - the "Schema" half of an <see cref="EntityFormModel"/>.</summary>
public sealed record EntitySchemaModel(string SchemaIri, IReadOnlyList<EntityFieldSchema> Fields);

/// <summary>One entity's current values by field name - the "Entity" half of an <see cref="EntityFormModel"/>.</summary>
public sealed record EntityDataModel(string EntityId, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// The "standard object" a UI form control renders from: a schema-driven entity screen's shape and
/// current data, bundled together but kept separate. Deliberately not <see cref="EntitySchema"/> or
/// <see cref="DynamicEntity"/> themselves - those are DAL/domain-shaped (a <see cref="DynamicEntity"/>
/// is a <see cref="System.Dynamic.DynamicObject"/> and doesn't serialize cleanly), and not a DataSet -
/// too heavy and table-centric for a single entity's form. Built by <see cref="From"/> from any
/// <see cref="EntitySchema"/>/<see cref="DynamicEntity"/> pair, so it works for any entity type, not
/// just <see cref="User"/>.
/// </summary>
public sealed record EntityFormModel(EntitySchemaModel Schema, EntityDataModel Entity)
{
    public static EntityFormModel From(EntitySchema schema, DynamicEntity entity)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entity);

        var fieldSchemas = schema.Fields.Values
            .Select(field => new EntityFieldSchema(field.Name, field.Type, field.IsRequired))
            .ToArray();

        var values = schema.Fields.Values.ToDictionary(
            field => field.Name,
            field => entity.GetValue(field.Name)?.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var entityId = entity.EntityId?.Value ?? throw new InvalidOperationException("Entity has no id.");

        return new EntityFormModel(
            new EntitySchemaModel(schema.SchemaIri, fieldSchemas),
            new EntityDataModel(entityId, values));
    }
}
