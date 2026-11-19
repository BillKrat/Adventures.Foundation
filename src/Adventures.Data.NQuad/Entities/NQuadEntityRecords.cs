using Adventures.Entities;

namespace Adventures.Data.NQuad;

/// <summary>The N-Quad implementation of <see cref="IEntityRecords"/>: reads through the registered entity types, honouring the tenant scope.</summary>
public sealed class NQuadEntityRecords(INQuadStore store, IEnumerable<EntityTypeRegistration> types, IGraphScope? scope = null) : IEntityRecords
{
    private readonly Dictionary<string, EntityTypeRegistration> _bySchema =
        types.GroupBy(t => t.Schema.SchemaIri, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public async Task<DynamicRecord?> GetAsync(string schemaIri, string id, CancellationToken cancellationToken = default) =>
        _bySchema.TryGetValue(schemaIri, out var type)
            ? await type.CreateRepository(store, scope).GetAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false)
            : null;

    public async Task<IReadOnlyList<DynamicRecord>> ListAsync(string schemaIri, CancellationToken cancellationToken = default) =>
        _bySchema.TryGetValue(schemaIri, out var type)
            ? await type.CreateRepository(store, scope).ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
            : [];
}
