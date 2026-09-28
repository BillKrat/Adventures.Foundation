using Adventures.Entities;

namespace Adventures.Data.NQuad;

/// <summary>
/// Reads a schema's field metadata from raw N-Quads and builds the storage-agnostic
/// <see cref="EntitySchema"/> that <see cref="DynamicEntity"/>/<see cref="NQuadEntityRepository{TEntity}"/>
/// need at construction time. This is the N-Quad-specific counterpart of what used to be
/// <c>EntitySchema.Load</c> - the triple-walking is Dal work (it knows the N-Quad shape a schema
/// is described in), not something the storage-agnostic Adventures.Entities project should know how
/// to do. Works for any schema IRI, not just the User schema - see <see cref="EntityConstants.Schema"/>
/// for the fixed predicate vocabulary every schema is described with.
/// </summary>
public static class SchemaDal
{
    public static EntitySchema Load(IEnumerable<NQuad> quads, string schemaIri)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaIri);

        var all = quads as NQuad[] ?? quads.ToArray();
        var schemaQuads = all.Where(q => q.Subject == schemaIri).ToArray();
        if (!schemaQuads.Any(q => q.Predicate == EntityConstants.Schema.TypePredicate && q.Object == EntityConstants.Schema.TypeIri))
        {
            throw new InvalidOperationException($"Schema '{schemaIri}' was not found in the supplied quads.");
        }

        var fields = new List<EntitySchemaField>();
        foreach (var fieldIri in schemaQuads
                     .Where(q => q.Predicate == EntityConstants.Schema.FieldPredicate)
                     .Select(q => q.Object)
                     .Distinct(StringComparer.Ordinal))
        {
            var fieldQuads = all.Where(q => q.Subject == fieldIri).ToArray();
            var fieldName = ReadRequired(fieldQuads, EntityConstants.Schema.FieldNamePredicate, fieldIri);
            var fieldType = ReadRequired(fieldQuads, EntityConstants.Schema.FieldTypePredicate, fieldIri);
            var fieldRdf = ReadRequired(fieldQuads, EntityConstants.Schema.FieldRdfPredicate, fieldIri);
            var valuePrefix = ReadOptional(fieldQuads, EntityConstants.Schema.FieldValuePrefixPredicate);
            fields.Add(new EntitySchemaField(schemaIri, fieldIri, fieldName, fieldType, fieldRdf, ValuePrefix: valuePrefix));
        }

        return EntitySchema.Create(schemaIri, fields);
    }

    private static string ReadRequired(IEnumerable<NQuad> quads, string predicate, string fieldIri)
    {
        var value = quads.Where(q => q.Predicate == predicate).Select(q => q.Object).SingleOrDefault();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Schema field '{fieldIri}' is missing '{predicate}'.");
    }

    private static string? ReadOptional(IEnumerable<NQuad> quads, string predicate) =>
        quads.Where(q => q.Predicate == predicate).Select(q => q.Object).SingleOrDefault();
}
