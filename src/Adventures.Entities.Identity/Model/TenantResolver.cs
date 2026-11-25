namespace Adventures.Entities;

/// <summary>How the current tenant was found: by the request's domain, by the development fallback, or not at all.</summary>
public enum TenantMatch
{
    None,
    Domain,
    Fallback,

    /// <summary>An authenticated person chose to act in this tenant (a tenant they belong to, or any tenant for a platform administrator); the host did not decide it.</summary>
    Acting,

    /// <summary>The host's own tenant has a mirror pointer, so the request is served from the tenant it points to (the answer carries the host's own tenant in <see cref="CurrentTenant.OwnId"/>).</summary>
    Mirror,
}

/// <summary>The tenant a request belongs to (when one was found) and the host it was read from.</summary>
/// <param name="OwnId">Only while mirroring (<see cref="TenantMatch.Mirror"/>): the tenant the host itself belongs to, whose pointer is set. Null otherwise.</param>
/// <param name="OwnDomain">The domain of that tenant.</param>
public sealed record CurrentTenant(string? Id, string? Name, string? Domain, string Host, TenantMatch Match, string? OwnId = null, string? OwnDomain = null);

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
            return Mirrored(found, all) is { } target ? DescribeMirror(target, found, host!) : Describe(found, host!, TenantMatch.Domain);
        }

        var fallback = Normalize(fallbackDomain);
        if (fallback.Length > 0 && Best(all, fallback) is { } viaFallback)
        {
            return Mirrored(viaFallback, all) is { } viaTarget ? DescribeMirror(viaTarget, viaFallback, host ?? string.Empty) : Describe(viaFallback, host ?? string.Empty, TenantMatch.Fallback);
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

    /// <summary>
    /// The tenant a mirror pointer on <paramref name="own"/> leads to, or null when there is no pointer or it cannot be followed: the target no longer exists (deleted tenants are not listed),
    /// is the tenant itself, or mirrors another tenant (one level only, so a chain or a loop is never followed and the host serves its own tenant).
    /// </summary>
    internal static Tenant? Mirrored(Tenant own, IReadOnlyList<Tenant> all)
    {
        var pointer = (own.GetValue("Mirrors") as string)?.Trim();
        if (string.IsNullOrEmpty(pointer))
        {
            return null;
        }

        var target = all.FirstOrDefault(t => t.EntityId?.Value == pointer);
        return target is null || target.EntityId?.Value == own.EntityId?.Value || !string.IsNullOrWhiteSpace(target.GetValue("Mirrors") as string) ? null : target;
    }

    private static CurrentTenant DescribeMirror(Tenant target, Tenant own, string host) =>
        new(target.EntityId?.Value, target.GetValue("Name") as string, target.GetValue("Domain") as string, host, TenantMatch.Mirror, own.EntityId?.Value, own.GetValue("Domain") as string);

    private static CurrentTenant Describe(Tenant tenant, string host, TenantMatch match) =>
        new(tenant.EntityId?.Value, tenant.GetValue("Name") as string, tenant.GetValue("Domain") as string, host, match);

    private static string Normalize(string? host) => TenantHosts.Normalize(host);
}
