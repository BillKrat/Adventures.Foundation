using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>What was removed, for the answer and the status line.</summary>
public sealed record TenantRemoval(string Id, string Name, string Domain, IReadOnlyList<string> Aliases);

/// <summary>
/// Deletes a tenant (a soft delete of the Tenant record: its domain and host names reach nothing afterwards, and the record can be recovered from a backup). The platform
/// administrator's act. It never deletes the tenant the caller is working in or reaching this host through (<paramref name="protectedTenantIds"/>), nor the last tenant, and it
/// needs the tenant's own domain typed as confirmation. The tenant's content stays in its own graph, unreachable; removing that is a separate, deliberate tool step.
/// </summary>
public interface ITenantRemover
{
    /// <summary>The removed tenant, or null when it does not exist. Throws <see cref="BusinessRuleException"/> with the reason when the deletion is refused (nothing is changed).</summary>
    Task<TenantRemoval?> RemoveAsync(string tenantId, string? confirmDomain, IReadOnlyCollection<string> protectedTenantIds, CancellationToken cancellationToken = default);
}

public sealed class TenantRemover(IEntityBll<Tenant> tenants, ITenantAliasEditor hosts) : ITenantRemover
{
    public async Task<TenantRemoval?> RemoveAsync(string tenantId, string? confirmDomain, IReadOnlyCollection<string> protectedTenantIds, CancellationToken cancellationToken = default)
    {
        var all = await hosts.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(t => string.Equals(t.Id, tenantId, StringComparison.Ordinal)) is not { } tenant)
        {
            return null;
        }

        if (protectedTenantIds.Contains(tenant.Id, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"{tenant.Name} is the tenant you are working in or reaching this site through, so it cannot be deleted now. Switch to another tenant, or open the site through another tenant's address, and try again.");
        }

        if (all.Count <= 1)
        {
            throw new BusinessRuleException($"{tenant.Name} is the last tenant. A site needs at least one tenant, so it cannot be deleted.");
        }

        var typed = TenantHosts.Normalize(confirmDomain);
        if (typed.Length == 0 || !string.Equals(typed, TenantHosts.Normalize(tenant.Domain), StringComparison.Ordinal))
        {
            throw new BusinessRuleException($"To delete {tenant.Name}, type its domain ({tenant.Domain}) to confirm. Nothing was deleted.");
        }

        if (!await tenants.DeleteAsync(tenant.Id, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TenantRemoval(tenant.Id, tenant.Name, tenant.Domain, tenant.Aliases);
    }
}
