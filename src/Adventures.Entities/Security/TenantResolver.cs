namespace Adventures.Entities;

/// <summary>How the current tenant was found: by the request's domain, by the development fallback, or not at all.</summary>
public enum TenantMatch
{
    None,
    Domain,
    Fallback,
}

/// <summary>The tenant a request belongs to (when one was found) and the host it was read from.</summary>
public sealed record CurrentTenant(string? Id, string? Name, string? Domain, string Host, TenantMatch Match);

/// <summary>
/// Works out which tenant a request is for from its host name, so one deployed app serves every tenant's domain: the code is updated once.
/// Read only; the host decides what to do with the answer (show it, scope data by it).
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// The tenant whose <c>Domain</c> or alias (<c>Aliases</c>, for example the host's temporary domain that mirrors the real one) is the host or that the host sits under (<c>api.</c>, <c>www.</c>); the longest matching name wins. A host
    /// that matches none has no tenant, unless <paramref name="fallbackDomain"/> (a development setting, for example for localhost) names one.
    /// </summary>
    Task<CurrentTenant> ResolveAsync(string? host, string? fallbackDomain = null, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the tenant from the <c>Domain</c> field of the Tenant entities.</summary>
public sealed class TenantResolver(IEntityBll<Tenant> tenants) : ITenantResolver
{
    public async Task<CurrentTenant> ResolveAsync(string? host, string? fallbackDomain = null, CancellationToken cancellationToken = default)
    {
        var name = Normalize(host);
        var all = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);
        if (name.Length > 0 && Best(all, name) is { } found)
        {
            return Describe(found, host!, TenantMatch.Domain);
        }

        var fallback = Normalize(fallbackDomain);
        if (fallback.Length > 0 && Best(all, fallback) is { } viaFallback)
        {
            return Describe(viaFallback, host ?? string.Empty, TenantMatch.Fallback);
        }

        return new CurrentTenant(null, null, null, host ?? string.Empty, TenantMatch.None);
    }

    /// <summary>The tenant with the longest domain or alias that the name equals or sits under (a dot boundary, never a bare suffix).</summary>
    private static Tenant? Best(IReadOnlyList<Tenant> all, string name) =>
        all.SelectMany(tenant => TenantAliasEditor.Names(tenant).Select(candidate => (Tenant: tenant, Domain: candidate)))
            .Where(pair => pair.Domain.Length > 0 && (name == pair.Domain || name.EndsWith("." + pair.Domain, StringComparison.Ordinal)))
            .OrderByDescending(pair => pair.Domain.Length)
            .Select(pair => pair.Tenant)
            .FirstOrDefault();

    private static CurrentTenant Describe(Tenant tenant, string host, TenantMatch match) =>
        new(tenant.EntityId?.Value, tenant.GetValue("Name") as string, tenant.GetValue("Domain") as string, host, match);

    private static string Normalize(string? host) => TenantHosts.Normalize(host);
}
