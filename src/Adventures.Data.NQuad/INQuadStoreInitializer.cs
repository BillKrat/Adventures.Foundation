namespace Adventures.Data.NQuad;

/// <summary>
/// One-time, idempotent setup for a store (for PostgreSQL: create the table and indexes; for the in-memory store: nothing).
/// Kept separate from <see cref="INQuadStore"/> so the store interface carries no provider-specific concept, while startup
/// code can still initialize whichever implementation is configured.
/// </summary>
public interface INQuadStoreInitializer
{
    /// <summary>Prepares the store for use. Safe to call repeatedly; never loses data.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
