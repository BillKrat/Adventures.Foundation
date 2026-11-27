namespace Adventures.Entities;

/// <summary>Where a catalog comes from: the store project reads it, a test makes it.</summary>
public interface ISchemaCatalogSource
{
    Task<SchemaCatalog> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Every schema, built from the data: nothing here names a particular area, schema or field.</summary>
public sealed class SchemaCatalog
{
    private readonly Dictionary<string, (EntitySchema Schema, string Area, string Name)> _schemas;
    private readonly IReadOnlyList<string> _unreadable;

    public SchemaCatalog(IEnumerable<EntitySchema> schemas, IEnumerable<string>? unreadable = null)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _schemas = new(StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            var (area, name) = Place(schema.SchemaIri);
            _schemas[schema.SchemaIri] = (schema, area, name);
        }

        _unreadable = (unreadable ?? []).Order(StringComparer.Ordinal).ToList();
    }

    public SchemaCatalogList List() =>
        new(Ordered().Select(s => new SchemaSummary(s.Schema.SchemaIri, s.Area, s.Name, s.Schema.Fields.Count)).ToList(), _unreadable);

    /// <summary>The schema with this id, or null.</summary>
    public SchemaDetail? Find(string schemaId)
    {
        if (string.IsNullOrEmpty(schemaId) || !_schemas.TryGetValue(schemaId, out var found))
        {
            return null;
        }

        var fields = InOrder(found.Schema)
            .Select(f => new SchemaFieldDescription(
                f.Name,
                string.IsNullOrWhiteSpace(f.Label) ? EntityFieldSchema.Humanize(f.Name) : f.Label,
                f.Type,
                f.Predicate,
                f.IsRequired,
                f.IsReadOnly,
                f.IsListVisible,
                EntityFieldSchema.RefSchemaOf(f.Type)))
            .ToList();
        var relations = found.Schema.Relations
            .Select(r => new SchemaRelationDescription(r.Name, r.Label, r.ChildSchemaIri, r.ViaPredicate, r.Reverse))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var usedBy = Ordered()
            .SelectMany(s => InOrder(s.Schema)
                .Where(f => string.Equals(EntityFieldSchema.RefSchemaOf(f.Type), schemaId, StringComparison.Ordinal))
                .Select(f => new SchemaUse(s.Schema.SchemaIri, s.Name, f.Name)))
            .ToList();

        return new SchemaDetail(schemaId, found.Area, found.Name, found.Schema.DisplayField, fields, relations, usedBy);
    }

    private IEnumerable<(EntitySchema Schema, string Area, string Name)> Ordered() =>
        _schemas.Values
            .OrderBy(s => s.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Schema.SchemaIri, StringComparer.Ordinal);

    private static IEnumerable<EntitySchemaField> InOrder(EntitySchema schema) =>
        schema.Fields.Values
            .OrderBy(f => f.Order ?? int.MaxValue)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The area and name of a schema: the last two parts of its address (path and fragment). An address with no parts is its own name.</summary>
    private static (string Area, string Name) Place(string id)
    {
        if (!Uri.TryCreate(id, UriKind.Absolute, out var address) || address.Host.Length == 0)
        {
            return (string.Empty, id);
        }

        var parts = (address.AbsolutePath + "/" + address.Fragment.TrimStart('#')).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (string.Empty, id),
            1 => (string.Empty, Uri.UnescapeDataString(parts[0])),
            _ => (Uri.UnescapeDataString(parts[^2]), Uri.UnescapeDataString(parts[^1])),
        };
    }
}
