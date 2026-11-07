using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>The tenant of the current request. It throws <see cref="TenantRequiredException"/> when the request has none, so nothing is read or changed for nobody.</summary>
public interface ITenantContext
{
    string TenantId { get; }
}

/// <summary>
/// Which tenants a person belongs to: a person belongs to a tenant when they hold a tenant role assignment for it, or an organisation assignment for an organisation of it
/// (an assignment with no role at all is a plain membership, which is how a newly added person joins). People are one shared identity across tenants; this is the wall
/// that keeps one tenant's administrators from seeing or changing another tenant's people.
/// </summary>
public interface IUserMembership
{
    /// <summary>The ids of every person who belongs to the tenant.</summary>
    Task<IReadOnlySet<string>> MemberIdsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The ids of every tenant the person belongs to.</summary>
    Task<IReadOnlySet<string>> TenantIdsOfAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Makes the person a member of the tenant without giving them a role (a tenant assignment with no role). Does nothing when they already belong.</summary>
    Task AddMemberAsync(string userId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Whether the organisation belongs to the tenant (an organisation assignment may only name an organisation of the administrator's own tenant).</summary>
    Task<bool> OrganisationInTenantAsync(string organisationId, string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The people an administrator of the current tenant may see and change: only members of that tenant. A person added here becomes a member. A person who also belongs to another
/// tenant cannot be deleted from here (that would remove them from the other tenant too). Sign-in and the profile use the plain <see cref="IUserBll"/>, which stays global.
/// Threat addressed: cross-tenant read and write of people.
/// </summary>
[Adventures.Ioc.Interfaces.ExcludeFromLifetimeScan]
public sealed class TenantScopedUserBll(IUserBll inner, IUserMembership membership, ITenantContext tenant) : IUserBll
{
    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default) => inner.FindByUserNameAsync(userName, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        var members = await membership.MemberIdsAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        return (await inner.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(user => user.EntityId is { } id && members.Contains(id.Value))
            .ToList();
    }

    public async Task<User?> GetAsync(string entityId, CancellationToken cancellationToken = default) =>
        await IsMemberAsync(entityId, cancellationToken).ConfigureAwait(false) ? await inner.GetAsync(entityId, cancellationToken).ConfigureAwait(false) : null;

    public async Task<User> CreateAsync(User entity, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId; // before anything is written: with no tenant nothing is created, not even an orphan person
        var created = await inner.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        await membership.AddMemberAsync(created.EntityId!.Value, tenantId, cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task UpdateAsync(User entity, CancellationToken cancellationToken = default)
    {
        if (entity.EntityId is not { } id || !await IsMemberAsync(id.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("No such person in this tenant.");
        }

        await inner.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (!await IsMemberAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if ((await membership.TenantIdsOfAsync(userId, cancellationToken).ConfigureAwait(false)).Any(id => !string.Equals(id, tenant.TenantId, StringComparison.Ordinal)))
        {
            throw new BusinessRuleException("This person also belongs to another tenant, so they cannot be deleted from here. Remove their roles in this tenant instead.");
        }

        return await inner.DeleteAsync(userId, currentUserId, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) => inner.DeleteAsync(entityId, cancellationToken);

    public Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) => inner.DeleteManyAsync(entityIds, cancellationToken);

    private async Task<bool> IsMemberAsync(string userId, CancellationToken cancellationToken) =>
        (await membership.MemberIdsAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false)).Contains(userId);
}

/// <summary>The people screen for an administrator of one tenant: <see cref="UserPresenter"/> over <see cref="TenantScopedUserBll"/>.</summary>
public interface ITenantUserPresenter : IUserPresenter;

[Adventures.Ioc.Interfaces.ExcludeFromLifetimeScan]
public sealed class TenantUserPresenter(TenantScopedUserBll bll, EntitySchemaOf<User> userSchema, IEntityRelationProvider? relations = null)
    : UserPresenter(bll, userSchema, relations), ITenantUserPresenter;
