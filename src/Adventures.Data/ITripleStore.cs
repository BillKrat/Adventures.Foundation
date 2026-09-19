namespace Adventures.Data;

/// <summary>
/// Access to W3C N-Quad style <see cref="Triple"/> statements — the extension/custom-field side
/// of the JSONB Hybrid + N-Quads storage model. Lets users design and populate arbitrary custom
/// form fields/grid columns at whatever granularity (tenant, org, user) the graph value encodes,
/// without requiring schema changes to the "standard fields" JSONB side (<see cref="IEntityRepository"/>).
/// </summary>
public interface ITripleStore
{
    /// <summary>Adds (or replaces, if an identical subject/predicate/graph statement exists) a triple.</summary>
    Task AddTripleAsync(Triple triple, CancellationToken cancellationToken = default);

    /// <summary>Gets all triples for a given subject, optionally restricted to a specific graph (tenant/org/user scope).</summary>
    Task<IReadOnlyList<Triple>> GetTriplesAsync(Guid subject, string? graph = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a specific subject/predicate/graph triple. Returns <c>false</c> if no matching triple existed.</summary>
    Task<bool> RemoveTripleAsync(Guid subject, string predicate, string graph, CancellationToken cancellationToken = default);
}
