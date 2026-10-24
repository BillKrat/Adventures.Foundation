// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
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
