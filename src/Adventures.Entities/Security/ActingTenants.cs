namespace Adventures.Entities;

/// <summary>
/// The tenants a signed-in person may act in, which is the answer behind the tenant switcher. A person may act in every tenant they belong to (<see cref="IUserMembership"/>);
/// a platform administrator may act in every tenant. Choosing a tenant changes only where the person works: their rights there are still the rights of the roles they hold in it
/// (a platform administrator with no role in a tenant holds only the platform right there). Threat addressed: acting in a tenant one does not belong to.
/// </summary>
public interface IActingTenants
{
    /// <summary>The tenants this person may act in, by name.</summary>
    Task<IReadOnlyList<TenantHostInfo>> ForAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>The tenant when this person may act in it; null when it does not exist or they may not.</summary>
    Task<TenantHostInfo?> AllowedAsync(string userName, string tenantId, CancellationToken cancellationToken = default);
}

public sealed class ActingTenants(IUserBll users, IUserMembership membership, IPlatformAdministrators platform, ITenantAliasEditor tenants) : IActingTenants
{
    public async Task<IReadOnlyList<TenantHostInfo>> ForAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || await users.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false) is not { EntityId: { } id })
        {
            return [];
        }

        var all = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);
        if (await platform.IsAsync(id.Value, cancellationToken).ConfigureAwait(false))
        {
            return all;
        }

        var mine = await membership.TenantIdsOfAsync(id.Value, cancellationToken).ConfigureAwait(false);
        return all.Where(tenant => mine.Contains(tenant.Id)).ToList();
    }

    public async Task<TenantHostInfo?> AllowedAsync(string userName, string tenantId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(tenantId)
            ? null
            : (await ForAsync(userName, cancellationToken).ConfigureAwait(false)).FirstOrDefault(tenant => string.Equals(tenant.Id, tenantId, StringComparison.Ordinal));
}
