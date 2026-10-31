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
    /// The tenant whose <c>Domain</c> is the host or that the host sits under (<c>api.</c>, <c>www.</c>); the longest matching domain wins. A host
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

    /// <summary>The tenant with the longest domain that the name equals or sits under (a dot boundary, never a bare suffix).</summary>
    private static Tenant? Best(IReadOnlyList<Tenant> all, string name) =>
        all.Select(tenant => (Tenant: tenant, Domain: Normalize(tenant.GetValue("Domain") as string)))
            .Where(pair => pair.Domain.Length > 0 && (name == pair.Domain || name.EndsWith("." + pair.Domain, StringComparison.Ordinal)))
            .OrderByDescending(pair => pair.Domain.Length)
            .Select(pair => pair.Tenant)
            .FirstOrDefault();

    private static CurrentTenant Describe(Tenant tenant, string host, TenantMatch match) =>
        new(tenant.EntityId?.Value, tenant.GetValue("Name") as string, tenant.GetValue("Domain") as string, host, match);

    /// <summary>Lower case, without a port, scheme or surrounding space or dots.</summary>
    private static string Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var text = host.Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            text = text[(scheme + 3)..];
        }

        var end = text.IndexOfAny(['/', ':', '?', '#']);
        return (end >= 0 ? text[..end] : text).Trim('.').ToLowerInvariant();
    }
}
