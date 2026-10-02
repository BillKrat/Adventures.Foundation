namespace Adventures.Entities;

public sealed record EntitySchemaField(
    string EntityType,
    string Id,
    string Name,
    string Type,
    string Predicate,
    bool IsRequired = false,
    string? ValuePrefix = null);

/// <summary>
/// Describes the fields of a dynamic entity type (e.g. "User"): their names, declared
/// types, and the source predicate each maps to. Deliberately has no dependency on any
/// particular storage engine (N-Quads, SQL, ...) - callers build the field list from
/// whatever their store produces (e.g. <c>Adventures.Data.NQuad.SchemaDal</c> for N-Quads)
/// and hand it to <see cref="Create"/>.
/// </summary>
public sealed class EntitySchema
{
    private EntitySchema(string schemaIri, IReadOnlyDictionary<string, EntitySchemaField> fields)
    {
        SchemaIri = schemaIri;
        Fields = fields;
        FieldsByPredicate = fields.Values.ToDictionary(
            field => field.Predicate,
            field => field,
            StringComparer.Ordinal);
    }

    public string SchemaIri { get; }

    public IReadOnlyDictionary<string, EntitySchemaField> Fields { get; }

    public IReadOnlyDictionary<string, EntitySchemaField> FieldsByPredicate { get; }

    /// <summary>
    /// Builds an <see cref="EntitySchema"/> from an already-parsed field list, enforcing the
    /// invariants every consumer (<see cref="DynamicEntity"/>, <c>NQuadEntityRepository{TEntity}</c>)
    /// relies on: at least one field, no two fields sharing a name, no two fields mapped to the same
    /// predicate. How the fields were discovered (N-Quads, SQL, ...) is not this type's concern.
    /// </summary>
    public static EntitySchema Create(string schemaIri, IEnumerable<EntitySchemaField> fields)
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

        return new EntitySchema(schemaIri, byName);
    }
}
