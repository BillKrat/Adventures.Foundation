using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// Sets or clears a tenant's mirror pointer ("use the configuration of"). The host calls it from an endpoint that needs the platform right: a pointer decides whose data a host
/// serves. The tenant's own record and data are never changed by a pointer, only ignored while it is set.
/// </summary>
public interface ITenantMirrors
{
    /// <summary>
    /// Points the tenant at <paramref name="targetId"/>, or clears the pointer when it is null or empty. False when the tenant does not exist. A <see cref="BusinessRuleException"/> (nothing
    /// changed) when the target does not exist, is the tenant itself, already mirrors another tenant, or when the tenant is itself the target of another one (one level only).
    /// </summary>
    Task<bool> SetAsync(string tenantId, string? targetId, CancellationToken cancellationToken = default);
}

public sealed class TenantMirrors(IEntityBll<Tenant> tenants) : ITenantMirrors
{
    public async Task<bool> SetAsync(string tenantId, string? targetId, CancellationToken cancellationToken = default)
    {
        var all = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(t => t.EntityId?.Value == tenantId) is not { } tenant)
        {
            return false;
        }

        var target = (targetId ?? string.Empty).Trim();
        if (target.Length > 0)
        {
            if (target == tenantId)
            {
                throw new BusinessRuleException("A site cannot use its own configuration as a mirror: choose another site, or none.");
            }

            if (all.FirstOrDefault(t => t.EntityId?.Value == target) is not { } other)
            {
                throw new BusinessRuleException("There is no such site to mirror.");
            }

            if (!string.IsNullOrWhiteSpace(other.GetValue("Mirrors") as string))
            {
                throw new BusinessRuleException($"{other.GetValue("Name") as string} is itself mirroring another site, so it cannot be mirrored. Choose the site it mirrors, or clear its mirror first.");
            }

            if (all.Any(t => t.EntityId?.Value != tenantId && string.Equals((t.GetValue("Mirrors") as string)?.Trim(), tenantId, StringComparison.Ordinal)))
            {
                throw new BusinessRuleException("Other sites mirror this one, so it cannot mirror a site itself. Clear their mirrors first.");
            }
        }

        var current = (tenant.GetValue("Mirrors") as string)?.Trim() ?? string.Empty;
        if (current != target)
        {
            tenant.Set("Mirrors", Guid.NewGuid().ToString(), target);
            await tenants.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
