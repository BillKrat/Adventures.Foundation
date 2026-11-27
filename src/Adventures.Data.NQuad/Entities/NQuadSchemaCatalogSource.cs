using Adventures.Entities;

namespace Adventures.Data.NQuad;

/// <summary>
/// Reads every schema of an N-Quad store: the subjects typed as a schema, then their fields and relations, never the whole store. Replaced values and
/// deleted schemas or fields are left out, as the entity repository leaves them out.
/// </summary>
public sealed class NQuadSchemaCatalogSource(INQuadStore store) : ISchemaCatalogSource
{
    public async Task<SchemaCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var typed = await store.QueryAsync(predicate: EntityConstants.Schema.TypePredicate, @object: EntityConstants.Schema.TypeIri, cancellationToken: cancellationToken).ConfigureAwait(false);
        var schemaIds = typed.Select(q => q.Subject).Distinct(StringComparer.Ordinal).ToList();
        if (schemaIds.Count == 0)
        {
            return new SchemaCatalog([]);
        }

        var schemaQuads = await store.QueryBySubjectsAsync(schemaIds, cancellationToken).ConfigureAwait(false);
        var childIds = schemaQuads
            .Where(q => q.Predicate is EntityConstants.Schema.FieldPredicate or EntityConstants.Schema.RelationPredicate)
            .Select(q => q.Object)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<NQuad> childQuads = childIds.Count == 0 ? [] : await store.QueryBySubjectsAsync(childIds, cancellationToken).ConfigureAwait(false);
        var all = schemaQuads.Concat(childQuads).ToList();

        // a replaced value is retired by a marker whose subject is that quad's id
        var markers = await store.QueryBySubjectsAsync(all.Select(q => q.Id.ToString()).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var replaced = markers.Where(m => m.Predicate == EntityConstants.Lifecycle.SupersededAtPredicate).Select(m => m.Subject).ToHashSet(StringComparer.Ordinal);
        var deleted = all.Where(q => q.Predicate == EntityConstants.Lifecycle.DeletedAtPredicate).Select(q => q.Subject).ToHashSet(StringComparer.Ordinal);

        var current = all
            .Where(q => !replaced.Contains(q.Id.ToString()) && !deleted.Contains(q.Subject))
            .Where(q => !(q.Predicate is EntityConstants.Schema.FieldPredicate or EntityConstants.Schema.RelationPredicate && deleted.Contains(q.Object)))
            .DistinctBy(q => (q.Subject, q.Predicate, q.Object)) // the same schema may be held in more than one graph
            .ToList();

        var schemas = new List<EntitySchema>();
        var unreadable = new List<string>();
        foreach (var id in schemaIds.Where(id => !deleted.Contains(id)))
        {
            try
            {
                schemas.Add(SchemaDal.Load(current, id));
            }
            catch (InvalidOperationException) // a schema that cannot be read is reported, and must not hide the others
            {
                unreadable.Add(id);
            }
        }

        return new SchemaCatalog(schemas, unreadable);
    }
}
