using System.Text.RegularExpressions;
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>Host-name rules shared by the resolver and the alias editor, so what is stored and what is matched are normalised the same way.</summary>
public static partial class TenantHosts
{
    /// <summary>Lower case, without a port, scheme, path or surrounding space or dots (the form every comparison uses).</summary>
    public static string Normalize(string? host)
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

    /// <summary>The aliases stored on a tenant (separated by commas, spaces or new lines), normalised, each once.</summary>
    public static IReadOnlyList<string> ParseAliases(string? stored) =>
        (stored ?? string.Empty)
            .Split([',', ' ', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Whether the text is a plain host name (letters, digits, dots and hyphens; at most 253 characters). A name decides whose data a request sees, so nothing else is accepted.</summary>
    public static bool IsValid(string host) => host.Length is > 0 and <= 253 && ValidHost().IsMatch(host);

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$")]
    private static partial Regex ValidHost();
}

/// <summary>One tenant with every host name that reaches it: its own domain and its aliases (for example the host's temporary domain that mirrors the real one).</summary>
public sealed record TenantHostInfo(string Id, string Name, string Domain, IReadOnlyList<string> Aliases);

/// <summary>
/// Which host names reach which tenant. The host calls it from endpoints that need the platform right (a host name decides whose data a request sees, so a tenant administrator
/// cannot add one). An alias is matched like the domain: the name itself or a subdomain of it.
/// </summary>
public interface ITenantAliasEditor
{
    /// <summary>Every tenant with its domain and aliases.</summary>
    Task<IReadOnlyList<TenantHostInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="host"/> as an alias of the tenant and returns the tenant's aliases, or null when the tenant does not exist. Throws <see cref="BusinessRuleException"/>
    /// when the name is not a plain host name or already reaches another tenant (its domain or alias, or a name under one), so two tenants can never claim one host.
    /// </summary>
    Task<IReadOnlyList<string>?> AddAsync(string tenantId, string host, CancellationToken cancellationToken = default);

    /// <summary>Removes the alias; returns the tenant's remaining aliases, or null when the tenant does not exist. The tenant's own domain is never an alias and cannot be removed here.</summary>
    Task<IReadOnlyList<string>?> RemoveAsync(string tenantId, string host, CancellationToken cancellationToken = default);
}

public sealed class TenantAliasEditor(IEntityBll<Tenant> tenants) : ITenantAliasEditor
{
    public async Task<IReadOnlyList<TenantHostInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        (await tenants.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(tenant => tenant.EntityId is not null)
            .Select(Describe)
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<string>?> AddAsync(string tenantId, string host, CancellationToken cancellationToken = default)
    {
        var alias = TenantHosts.Normalize(host);
        if (!TenantHosts.IsValid(alias))
        {
            throw new BusinessRuleException("That is not a host name. Use letters, digits, dots and hyphens only, for example www.example.com.");
        }

        var all = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(t => t.EntityId?.Value == tenantId) is not { } tenant)
        {
            return null;
        }

        foreach (var other in all.Where(t => t.EntityId?.Value != tenantId))
        {
            if (Reaches(other, alias))
            {
                throw new BusinessRuleException($"{alias} already reaches {other.GetValue("Name") as string}. A host name can belong to only one tenant.");
            }
        }

        var current = TenantHosts.ParseAliases(tenant.GetValue("Aliases") as string);
        if (current.Contains(alias, StringComparer.Ordinal) || alias == TenantHosts.Normalize(tenant.GetValue("Domain") as string))
        {
            return current;
        }

        var updated = current.Append(alias).ToList();
        tenant.Set("Aliases", Guid.NewGuid().ToString(), string.Join(',', updated));
        await tenants.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<IReadOnlyList<string>?> RemoveAsync(string tenantId, string host, CancellationToken cancellationToken = default)
    {
        if (await tenants.GetAsync(tenantId, cancellationToken).ConfigureAwait(false) is not { } tenant)
        {
            return null;
        }

        var alias = TenantHosts.Normalize(host);
        var current = TenantHosts.ParseAliases(tenant.GetValue("Aliases") as string);
        var updated = current.Where(existing => !string.Equals(existing, alias, StringComparison.Ordinal)).ToList();
        if (updated.Count != current.Count)
        {
            tenant.Set("Aliases", Guid.NewGuid().ToString(), string.Join(',', updated));
            await tenants.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);
        }

        return updated;
    }

    /// <summary>Whether the tenant already reaches this host: through its domain or an alias, either equal or above it (a host under a name the tenant holds is already the tenant's).</summary>
    private static bool Reaches(Tenant tenant, string host) =>
        Names(tenant).Any(name => host == name || host.EndsWith("." + name, StringComparison.Ordinal) || name.EndsWith("." + host, StringComparison.Ordinal));

    internal static IEnumerable<string> Names(Tenant tenant) =>
        TenantHosts.ParseAliases(tenant.GetValue("Domain") as string).Concat(TenantHosts.ParseAliases(tenant.GetValue("Aliases") as string));

    private static TenantHostInfo Describe(Tenant tenant) =>
        new(tenant.EntityId!.Value, tenant.GetValue("Name") as string ?? string.Empty, tenant.GetValue("Domain") as string ?? string.Empty, TenantHosts.ParseAliases(tenant.GetValue("Aliases") as string));
}
