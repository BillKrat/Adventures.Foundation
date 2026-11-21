using Microsoft.Extensions.Primitives;

namespace Adventures.WebApi;

/// <summary>
/// One switch that clears everything kept about hosts and tenants (which tenant a host reaches, which sites the API answers). The caches are short so a change is
/// seen within a minute anyway; the endpoints that change a host name call <see cref="Invalidate"/>, so the person who saved sees the change at once and does not
/// think they did something wrong. Entries that carry <see cref="Token"/> expire when it fires. Per process: a second server instance would still wait out the minute.
/// </summary>
public sealed class TenantCacheInvalidator
{
    private readonly object _gate = new();
    private CancellationTokenSource _source = new();

    /// <summary>A token that fires on the next <see cref="Invalidate"/>; ask again after it has fired.</summary>
    public IChangeToken Token
    {
        get
        {
            lock (_gate)
            {
                return new CancellationChangeToken(_source.Token);
            }
        }
    }

    /// <summary>Expires every entry that was kept with the current token.</summary>
    public void Invalidate()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            old = _source;
            _source = new CancellationTokenSource();
        }

        old.Cancel();
        old.Dispose();
    }
}
