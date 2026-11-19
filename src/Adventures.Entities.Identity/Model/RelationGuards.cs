using Adventures.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Entities;

/// <summary>
/// A tenant must keep at least one administrator: removing the last tenant role assignment that gives the Admin role in a
/// tenant is refused. This guards the role DATA; what an administrator may do on the API is still decided by the identity
/// roles in the token until the identity store moves onto these assignments.
/// </summary>
public sealed class LastTenantAdministratorGuard(IEntityRecords records) : IRelationChangeGuard
{
    public const string AdminRoleName = "Admin";

    public Task CheckAddAsync(RelationChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task CheckRemoveAsync(RelationChange change, CancellationToken cancellationToken = default)
    {
        if (change.Relation.ChildSchemaIri != IdentityConstants.TenantRoleSchemaIri
            || !change.ChildValues.TryGetValue("Role", out var roleId) || !change.ChildValues.TryGetValue("Tenant", out var tenantId)
            || roleId is null || tenantId is null)
        {
            return;
        }

        var role = await records.GetAsync(IdentityConstants.RoleSchemaIri, roleId, cancellationToken).ConfigureAwait(false);
        if (role?.GetValue("Name") as string != AdminRoleName)
        {
            return;
        }

        var others = (await records.ListAsync(IdentityConstants.TenantRoleSchemaIri, cancellationToken).ConfigureAwait(false))
            .Count(a => a.EntityId?.Value != change.ChildId
                && string.Equals(a.GetValue("Role") as string, roleId, StringComparison.Ordinal)
                && string.Equals(a.GetValue("Tenant") as string, tenantId, StringComparison.Ordinal));
        if (others == 0)
        {
            throw new BusinessRuleException("A tenant must keep at least one administrator: this is the last Admin assignment for it.");
        }
    }
}

/// <summary>
/// Nobody removes their own role assignment when that role lets them manage users or roles: it would lock them out of the very screen they
/// are using, and a mistaken click is hard to undo without another administrator. Another administrator can still remove it, and the
/// last-administrator guard still protects the tenant. Threat addressed: accidental self-lockout from role administration.
/// </summary>
public sealed class SelfLockoutGuard(IEntityRecords records) : IRelationChangeGuard
{
    public Task CheckAddAsync(RelationChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task CheckRemoveAsync(RelationChange change, CancellationToken cancellationToken = default)
    {
        if (change.Relation.ChildSchemaIri is not (IdentityConstants.TenantRoleSchemaIri or IdentityConstants.OrganizationRoleSchemaIri)
            || string.IsNullOrWhiteSpace(change.ActingUserName)
            || !change.ChildValues.TryGetValue("Role", out var roleId) || string.IsNullOrEmpty(roleId))
        {
            return;
        }

        var user = await records.GetAsync(change.ParentSchema.SchemaIri, change.ParentId, cancellationToken).ConfigureAwait(false);
        if (user is null || !string.Equals(user.GetValue("UserName") as string, change.ActingUserName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var role = await records.GetAsync(IdentityConstants.RoleSchemaIri, roleId, cancellationToken).ConfigureAwait(false);
        var rights = Rights.Parse(role?.GetValue("Rights") as string);
        if (rights.Contains(Rights.UsersManage) || rights.Contains(Rights.RolesManage))
        {
            throw new BusinessRuleException("You cannot remove your own role that lets you manage users or roles. Ask another administrator to do it.");
        }
    }
}

public static class IdentityRelationGuardExtensions
{
    /// <summary>Registers the identity rules over relation changes: the last tenant administrator stays, and nobody removes from themself the role that lets them manage users or roles.</summary>
    public static IServiceCollection AddIdentityRelationGuards(this IServiceCollection services)
    {
        services.AddScoped<IRelationChangeGuard, LastTenantAdministratorGuard>();
        services.AddScoped<IRelationChangeGuard, SelfLockoutGuard>();
        return services;
    }
}
