namespace Adventures.Entities;

/// <summary>
/// The business-rules layer for <see cref="SchemaEntity"/>/<see cref="SchemaFieldEntity"/> -
/// storage-agnostic, depends only on <see cref="IEntityRepository{TEntity}"/>. Two jobs:
/// <see cref="LoadEntitySchemaAsync"/> composes a <see cref="SchemaEntity"/> and its
/// <see cref="SchemaFieldEntity"/> instances (fetched through the ordinary, unmodified generic
/// repositories - no separate Dal wrapper needed, since <c>NQuadEntityRepository{SchemaEntity}</c>
/// already is the Dal) into an <see cref="EntitySchema"/>; <see cref="ToEntitySchema"/> is the
/// synchronous translation, checking each field carries the literal values (Name/FieldType/
/// Predicate) a well-formed field definition needs - something <see cref="EntitySchema.Create"/>
/// itself can't check, since it only ever sees already-built <see cref="EntitySchemaField"/> records.
/// </summary>
public sealed class SchemaBll
{
    private readonly IEntityRepository<SchemaEntity> _schemas;
    private readonly IEntityRepository<SchemaFieldEntity> _fields;

    public SchemaBll(IEntityRepository<SchemaEntity> schemas, IEntityRepository<SchemaFieldEntity> fields)
    {
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    /// <summary>
    /// Fetches <paramref name="schemaEntityId"/> and every <see cref="SchemaFieldEntity"/> its
    /// "Field" values reference, then builds the <see cref="EntitySchema"/> other entities need at
    /// construction time. Only works for a schema created through the new, generic-repository
    /// convention - a legacy, hand-seeded schema (still read via <c>Adventures.Data.NQuad.SchemaDal</c>)
    /// has no <see cref="SchemaFieldEntity"/> instances under this convention's base IRI to fetch.
    /// </summary>
    public async Task<EntitySchema> LoadEntitySchemaAsync(string schemaEntityId, CancellationToken cancellationToken = default)
    {
        var schema = await _schemas.GetAsync(schemaEntityId, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Schema '{schemaEntityId}' was not found.");

        var fields = new List<SchemaFieldEntity>();
        foreach (var fieldValue in schema.GetValues("Field"))
        {
            var fieldEntityId = StripFieldEntityBaseIri(fieldValue.ToString()!);
            var field = await _fields.GetAsync(fieldEntityId, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Schema field '{fieldValue}' was not found.");
            fields.Add(field);
        }

        return ToEntitySchema(schema, fields);
    }

    public static EntitySchema ToEntitySchema(SchemaEntity schema, IReadOnlyList<SchemaFieldEntity> fields)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(fields);

        var schemaId = schema.EntityId?.Value ?? throw new InvalidOperationException("Schema has no entity id.");
        var entitySchemaFields = fields.Select(field => ToEntitySchemaField(schemaId, field)).ToArray();
        return EntitySchema.Create(schemaId, entitySchemaFields);
    }

    private static EntitySchemaField ToEntitySchemaField(string schemaEntityType, SchemaFieldEntity field)
    {
        var fieldId = field.EntityId?.Value ?? throw new InvalidOperationException("Schema field has no entity id.");
        var name = RequireValue(field, "Name", fieldId);
        var fieldType = RequireValue(field, "FieldType", fieldId);
        var predicate = RequireValue(field, "Predicate", fieldId);
        var valuePrefix = field.GetValue("ValuePrefix")?.ToString();
        return new EntitySchemaField(schemaEntityType, fieldId, name, fieldType, predicate, ValuePrefix: valuePrefix);
    }

    private static string RequireValue(SchemaFieldEntity field, string propertyName, string fieldId) =>
        field.GetValue(propertyName)?.ToString() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Schema field '{fieldId}' is missing '{propertyName}'.");

    private static string StripFieldEntityBaseIri(string fieldIri) =>
        fieldIri.StartsWith(EntityConstants.Schema.FieldEntityBaseIri, StringComparison.Ordinal)
            ? fieldIri[EntityConstants.Schema.FieldEntityBaseIri.Length..]
            : throw new InvalidOperationException(
                $"Schema field IRI '{fieldIri}' is not under the expected base IRI '{EntityConstants.Schema.FieldEntityBaseIri}'.");
}
