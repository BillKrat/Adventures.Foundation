// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Data.NQuad;

/// <summary>
/// Store maintenance for <see cref="NQuad"/> rows: insert, query by terms, count, purge. Implemented by
/// <see cref="NpgsqlNQuadStore"/> (PostgreSQL) and <see cref="InMemoryNQuadStore"/> (dev and tests) so callers and the
/// shared contract tests depend only on this interface. Seeding from a ".nq" file is the extension method
/// <see cref="NQuadStoreExtensions.SeedFromFileAsync"/>; table/collection setup is <see cref="INQuadStoreInitializer"/>.
/// By-id operations and CRUDL for DynamicEntity classes are a separate layer built on top of this one.
/// </summary>
/// <remarks>
/// Contract: a null term in <see cref="QueryAsync"/> matches everything; a null <see cref="NQuad.Graph"/> is the default
/// graph; inserting a duplicate id throws (the exception type is defined by the separate exception-handling design) and
/// adds nothing; <see cref="InsertManyAsync"/> is all-or-nothing; an empty batch returns 0; an already-cancelled token
/// throws <see cref="OperationCanceledException"/>; result order is not defined.
/// </remarks>
public interface INQuadStore
{
    /// <summary>Inserts a single quad. Returns the number of rows inserted.</summary>
    Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default);

    /// <summary>Inserts a batch of quads atomically. Returns the number of rows inserted.</summary>
    Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default);

    /// <summary>Returns every quad, optionally filtered by any combination of terms (null means any).</summary>
    Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every quad whose subject is any of <paramref name="subjects"/>, in one round trip. This is the bulk read the
    /// entity layer needs (list N entities, resolve their superseded markers) without one query per entity. An empty
    /// set returns an empty list.
    /// </summary>
    Task<IReadOnlyList<NQuad>> QueryBySubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default);

    /// <summary>Returns the total number of quads.</summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes every quad, leaving the store itself usable.</summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);
}
