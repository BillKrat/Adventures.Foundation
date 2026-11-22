namespace Adventures.Entities;

/// <summary>
/// The first site of a fresh install: when the store holds no tenant at all, the host the first request came on becomes the first tenant with its own Admin (default password, forced to
/// change at the first sign-in) and the platform switch. A store that has any tenant is never touched, so a running install cannot be taken over this way.
/// </summary>
public interface IFirstRunSite
{
    /// <summary>True when the site was created now; false (nothing changed) when a tenant already exists or the host is not a real host name.</summary>
    Task<bool> TryCreateFirstSiteAsync(string host, CancellationToken cancellationToken = default);
}

/// <summary>For a host with no sign-in store (the Legacy developer mode): there is no first site to make.</summary>
public sealed class NoFirstRunSite : IFirstRunSite
{
    public Task<bool> TryCreateFirstSiteAsync(string host, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
