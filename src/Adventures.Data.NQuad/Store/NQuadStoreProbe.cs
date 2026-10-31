using Adventures.Common.Diagnostics;

namespace Adventures.Data.NQuad;

/// <summary>The N-Quad store's <see cref="IStoreProbe"/>: one count against the store.</summary>
public sealed class NQuadStoreProbe(INQuadStore store) : IStoreProbe
{
    public Task CheckAsync(CancellationToken cancellationToken = default) => store.CountAsync(cancellationToken);
}
