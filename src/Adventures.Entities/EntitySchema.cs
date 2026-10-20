namespace Adventures.Entities;

public sealed record EntitySchemaField(
    string EntityType,
    string Id,
    string Name,
    string Type,
    string Predicate,
    bool IsRequired = false,
    string? ValuePrefix = null,
    string? Label = null,
    bool IsReadOnly = false,
    int? Order = null,
    bool IsListVisible = true,
    string? DefaultValue = null);

/// <summary>
/// Describes the fields of a dynamic entity type (e.g. "User"): their names, declared
/// types, and the source predicate each maps to. Deliberately has no dependency on any
/// particular storage engine (N-Quads, SQL, ...) - callers build the field list from
/// whatever their store produces (e.g. <c>Adventures.Data.NQuad.SchemaDal</c> for N-Quads)
/// and hand it to <see cref="Create"/>.
/// </summary>
public sealed class EntitySchema
{
    private EntitySchema(string schemaIri, IReadOnlyDictionary<string, EntitySchemaField> fields, IReadOnlyList<EntityRelationDefinition> relations, string? displayField)
    {
        SchemaIri = schemaIri;
        Fields = fields;
        Relations = relations;
        DisplayField = displayField;
        FieldsByPredicate = fields.Values.ToDictionary(
            field => field.Predicate,
            field => field,
            StringComparer.Ordinal);
    }

    public string SchemaIri { get; }

    public IReadOnlyDictionary<string, EntitySchemaField> Fields { get; }

    public IReadOnlyDictionary<string, EntitySchemaField> FieldsByPredicate { get; }

    /// <summary>The child collections this schema declares (empty for most entities).</summary>
    public IReadOnlyList<EntityRelationDefinition> Relations { get; }

    /// <summary>The field that names an entity of this schema, when the schema declares one.</summary>
    public string? DisplayField { get; }

    /// <summary>
    /// Builds an <see cref="EntitySchema"/> from an already-parsed field list, enforcing the
    /// invariants every consumer (<see cref="DynamicEntity"/>, <c>NQuadEntityRepository{TEntity}</c>)
    /// relies on: at least one field, no two fields sharing a name, no two fields mapped to the same
    /// predicate. How the fields were discovered (N-Quads, SQL, ...) is not this type's concern.
    /// </summary>
    public static EntitySchema Create(string schemaIri, IEnumerable<EntitySchemaField> fields, IEnumerable<EntityRelationDefinition>? relations = null, string? displayField = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaIri);
        ArgumentNullException.ThrowIfNull(fields);

        var byName = new Dictionary<string, EntitySchemaField>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (!byName.TryAdd(field.Name, field))
            {
                throw new InvalidOperationException($"Schema '{schemaIri}' defines the field '{field.Name}' more than once.");
            }
        }

        if (byName.Count == 0)
        {
            throw new InvalidOperationException($"Schema '{schemaIri}' does not define any fields.");
        }

        if (byName.Values.GroupBy(field => field.Predicate, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException($"Schema '{schemaIri}' maps more than one field to the same predicate.");
        }

        return new EntitySchema(schemaIri, byName, relations?.ToArray() ?? [], displayField);
    }
}
