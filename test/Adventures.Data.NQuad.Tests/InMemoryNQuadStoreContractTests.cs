namespace Adventures.Data.NQuad.Tests;

/// <summary>Runs the shared store contract against the in-memory store. Always runs; needs no database.</summary>
public sealed class InMemoryNQuadStoreContractTests : NQuadStoreContractTests
{
    protected override Task<INQuadStore?> CreateStoreAsync() => Task.FromResult<INQuadStore?>(new InMemoryNQuadStore());
}
