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
/// particular storage engine (N-Quads, SQL, ...) - callers build an <see cref="EntitySchema"/>
/// from whatever raw (subject, predicate, object) triples their store produces via
/// <see cref="Load"/>.
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
    /// Builds an <see cref="EntitySchema"/> from raw (subject, predicate, object) triples
    /// describing a schema and its fields. <paramref name="typePredicate"/>/<paramref name="typeIri"/>
    /// identify the "this subject is a Schema" triple; the remaining predicate constants
    /// identify a schema's field list and each field's name/type/mapped-predicate/prefix.
    /// </summary>
    public static EntitySchema Load(
        IEnumerable<(string Subject, string Predicate, string Object)> triples,
        string schemaIri,
        string typePredicate,
        string typeIri,
        string fieldPredicate,
        string fieldNamePredicate,
        string fieldTypePredicate,
        string fieldRdfPredicate,
        string? fieldValuePrefixPredicate = null)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaIri);

        var all = triples as (string Subject, string Predicate, string Object)[] ?? triples.ToArray();
        var schemaTriples = all.Where(t => t.Subject == schemaIri).ToArray();
        if (!schemaTriples.Any(t => t.Predicate == typePredicate && t.Object == typeIri))
        {
            throw new InvalidOperationException($"Schema '{schemaIri}' was not found in the supplied triples.");
        }

        var fields = new Dictionary<string, EntitySchemaField>(StringComparer.OrdinalIgnoreCase);
        foreach (var fieldIri in schemaTriples
                     .Where(t => t.Predicate == fieldPredicate)
                     .Select(t => t.Object)
                     .Distinct(StringComparer.Ordinal))
        {
            var fieldTriples = all.Where(t => t.Subject == fieldIri).ToArray();
            var fieldName = ReadRequired(fieldTriples, fieldNamePredicate, fieldIri);
            var fieldType = ReadRequired(fieldTriples, fieldTypePredicate, fieldIri);
            var fieldRdf = ReadRequired(fieldTriples, fieldRdfPredicate, fieldIri);
            var valuePrefix = fieldValuePrefixPredicate is null ? null : ReadOptional(fieldTriples, fieldValuePrefixPredicate);
            if (!fields.TryAdd(fieldName, new EntitySchemaField(schemaIri, fieldIri, fieldName, fieldType, fieldRdf, ValuePrefix: valuePrefix)))
            {
                throw new InvalidOperationException($"Schema '{schemaIri}' defines the field '{fieldName}' more than once.");
            }
        }

        if (fields.Count == 0)
        {
            throw new InvalidOperationException($"Schema '{schemaIri}' does not define any fields.");
        }

        if (fields.Values.GroupBy(f => f.Predicate, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException($"Schema '{schemaIri}' maps more than one field to the same predicate.");
        }

        return new EntitySchema(schemaIri, fields);
    }

    private static string ReadRequired(
        IEnumerable<(string Subject, string Predicate, string Object)> triples,
        string predicate,
        string fieldIri)
    {
        var value = triples
            .Where(t => t.Predicate == predicate)
            .Select(t => t.Object)
            .SingleOrDefault();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Schema field '{fieldIri}' is missing '{predicate}'.");
    }

    private static string? ReadOptional(IEnumerable<(string Subject, string Predicate, string Object)> triples, string predicate) =>
        triples
            .Where(t => t.Predicate == predicate)
            .Select(t => t.Object)
            .SingleOrDefault();
}
