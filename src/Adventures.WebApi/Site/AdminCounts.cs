using Microsoft.Extensions.Caching.Memory;

namespace Adventures.WebApi;

/// <summary>One count a feature owns (the active exceptions, the comments waiting for approval, the collaboration items waiting for review).</summary>
public interface IAdminCountSource
{
    /// <summary>The menu entry id it is shown beside; also the key the dashboard asks for.</summary>
    string Id { get; }

    Task<int> CountAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The counts of every registered source, each remembered for five seconds per tenant (short on purpose: the client refreshes the badge right after an approval, so a longer wait would show a stale number) (R3 of the host reuse review: the menu used to page through every post and list every
/// collaboration item on every menu load; a menu load and a dashboard load in the same moment now share one answer).
/// </summary>
public sealed class AdminCounts(IEnumerable<IAdminCountSource> sources, IMemoryCache cache, Func<string> tenantKey)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, IAdminCountSource> _sources = sources.ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>The count for an id (0 when no feature contributes it).</summary>
    public async Task<int> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (!_sources.TryGetValue(id, out var source))
        {
            return 0;
        }

        var key = $"admin-count:{tenantKey()}:{id}";
        if (cache.TryGetValue(key, out int remembered))
        {
            return remembered;
        }

        var count = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        cache.Set(key, count, Lifetime);
        return count;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in _sources.Keys)
        {
            all[id] = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return all;
    }
}
