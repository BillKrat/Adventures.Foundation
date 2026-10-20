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
            fields.Add(new EntitySchemaField(
                schemaIri, fieldIri, fieldName, fieldType, fieldRdf,
                IsRequired: ReadFlag(fieldQuads, EntityConstants.Schema.FieldRequiredPredicate, false),
                ValuePrefix: valuePrefix,
                Label: ReadOptional(fieldQuads, EntityConstants.Schema.FieldLabelPredicate),
                IsReadOnly: ReadFlag(fieldQuads, EntityConstants.Schema.FieldReadOnlyPredicate, false),
                Order: int.TryParse(ReadOptional(fieldQuads, EntityConstants.Schema.FieldOrderPredicate), out var order) ? order : null,
                IsListVisible: ReadFlag(fieldQuads, EntityConstants.Schema.FieldListVisiblePredicate, true)));
        }

        var relations = new List<EntityRelationDefinition>();
        foreach (var relationIri in schemaQuads
                     .Where(q => q.Predicate == EntityConstants.Schema.RelationPredicate)
                     .Select(q => q.Object)
                     .Distinct(StringComparer.Ordinal))
        {
            var relationQuads = all.Where(q => q.Subject == relationIri).ToArray();
            var name = ReadRequired(relationQuads, EntityConstants.Schema.RelationNamePredicate, relationIri);
            relations.Add(new EntityRelationDefinition(
                name,
                ReadOptional(relationQuads, EntityConstants.Schema.RelationLabelPredicate) ?? EntityFieldSchema.Humanize(name),
                ReadRequired(relationQuads, EntityConstants.Schema.RelationChildPredicate, relationIri),
                ReadRequired(relationQuads, EntityConstants.Schema.RelationViaPredicate, relationIri),
                string.Equals(ReadOptional(relationQuads, EntityConstants.Schema.RelationDirectionPredicate), "reverse", StringComparison.OrdinalIgnoreCase)));
        }

        return EntitySchema.Create(schemaIri, fields, relations, ReadOptional(schemaQuads, EntityConstants.Schema.DisplayFieldPredicate));
    }

    private static string ReadRequired(IEnumerable<NQuad> quads, string predicate, string fieldIri)
    {
        var value = quads.Where(q => q.Predicate == predicate).Select(q => q.Object).SingleOrDefault();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Schema field '{fieldIri}' is missing '{predicate}'.");
    }

    private static bool ReadFlag(IEnumerable<NQuad> quads, string predicate, bool fallback) =>
        bool.TryParse(ReadOptional(quads, predicate), out var flag) ? flag : fallback;

    private static string? ReadOptional(IEnumerable<NQuad> quads, string predicate) =>
        quads.Where(q => q.Predicate == predicate).Select(q => q.Object).SingleOrDefault();
}
