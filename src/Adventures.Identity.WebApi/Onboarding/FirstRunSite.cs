using Adventures.Entities;
using Adventures.Identity;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Creates the first site of a fresh install (see <see cref="IFirstRunSite"/>): the sign-in account first (it is safe to repeat), then the Admin person, the tenant, the person's
/// membership and the platform switch. One process-wide lock plus a second look at the tenant list inside it means two first requests at the same moment make one site.
/// </summary>
public sealed class FirstRunSite(
    ITenantAliasEditor tenants, ITenantCreator creator, IAccountCreator accounts, IUserBll people, IUserMembership membership, IFirstPlatformAdministrator platform, EntitySchemaOf<User> userSchema) : IFirstRunSite
{
    public const string AdminUserName = "Admin";
    public const string DefaultPassword = "Admin";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// The site's domain from the host a request came on. The site is on the domain and the API on a subdomain (`api.foo.com`), and the first request is often the API's, so a leading
    /// `api.`, `www.` or `mcp.` is dropped when a real domain (a dot) remains. Any other name is kept as it is (a temporary address, or `shop.foo.com`).
    /// </summary>
    internal static string SiteDomainOf(string host)
    {
        foreach (var prefix in new[] { "api.", "www.", "mcp." })
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal) && host[prefix.Length..].Contains('.'))
            {
                return host[prefix.Length..];
            }
        }

        return host;
    }

    public async Task<bool> TryCreateFirstSiteAsync(string host, CancellationToken cancellationToken = default)
    {
        var name = SiteDomainOf(TenantHosts.Normalize(host));
        if (!TenantHosts.IsRealName(name) || (await tenants.ListAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return false;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if ((await tenants.ListAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return false;   // another request got there first
            }

            await new SiteProvisioner(tenants, creator, accounts, people, membership, userSchema).CreateAsync(name, name, cancellationToken).ConfigureAwait(false);
            await platform.EnsureFirstAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
