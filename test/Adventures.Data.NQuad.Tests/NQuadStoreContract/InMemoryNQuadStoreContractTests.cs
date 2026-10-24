// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Data.NQuad.Tests;

/// <summary>Runs the shared store contract against the in-memory store. Always runs; needs no database.</summary>
public sealed class InMemoryNQuadStoreContractTests : NQuadStoreContractTests
{
    protected override Task<INQuadStore?> CreateStoreAsync() => Task.FromResult<INQuadStore?>(new InMemoryNQuadStore());
}
