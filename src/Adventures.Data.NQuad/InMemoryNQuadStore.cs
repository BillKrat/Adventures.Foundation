namespace Adventures.Data.NQuad;

/// <summary>
/// Thread-safe in-memory <see cref="INQuadStore"/> for development and tests (no database needed). Ported from the POC's
/// InMemoryQuadrupleStore and reshaped to the shared async contract. Rows keep insertion order, though callers must not
/// depend on order.
/// </summary>
public sealed class InMemoryNQuadStore : INQuadStore, INQuadStoreInitializer
{
    private readonly object _gate = new();
    private readonly List<NQuad> _rows = [];
    private readonly HashSet<Guid> _ids = [];

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quad);
        return InsertManyAsync([quad], cancellationToken);
    }

    public Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        cancellationToken.ThrowIfCancellationRequested();
        if (quads.Count == 0)
        {
            return Task.FromResult(0);
        }

        lock (_gate)
        {
            // Validate the whole batch first so a duplicate id leaves the store untouched (all-or-nothing).
            var batchIds = new HashSet<Guid>();
            foreach (var quad in quads)
            {
                ArgumentNullException.ThrowIfNull(quad);
                if (_ids.Contains(quad.Id) || !batchIds.Add(quad.Id))
                {
                    throw new InvalidOperationException("A quad with this ID already exists.");
                }
            }

            _rows.AddRange(quads);
            _ids.UnionWith(batchIds);
            return Task.FromResult(quads.Count);
        }
    }

    public Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<NQuad> result = _rows
                .Where(q => (subject is null || q.Subject == subject)
                    && (predicate is null || q.Predicate == predicate)
                    && (@object is null || q.Object == @object)
                    && (graph is null || q.Graph == graph))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult((long)_rows.Count);
        }
    }

    public Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var removed = _rows.Count;
            _rows.Clear();
            _ids.Clear();
            return Task.FromResult(removed);
        }
    }
}
