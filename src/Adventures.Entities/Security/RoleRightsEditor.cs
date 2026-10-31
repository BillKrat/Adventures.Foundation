using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>Changes what a role allows. The host calls it from the roles endpoint (itself guarded by <see cref="Rights.RolesManage"/>).</summary>
public interface IRoleRightsEditor
{
    /// <summary>
    /// Stores <paramref name="rights"/> (each once, in catalog order) as the rights of the role and returns the stored text, or null when the role does not exist.
    /// Throws <see cref="BusinessRuleException"/> for an unknown right, for a change that would take away the acting person's own ability to manage users and
    /// roles, and for one that would leave nobody with it.
    /// </summary>
    Task<string?> SetRightsAsync(string roleId, IReadOnlyCollection<string> rights, string actingUserName, CancellationToken cancellationToken = default);
}

public sealed class RoleRightsEditor(IEntityBll<Role> roles, IUserBll users, IRightsResolver resolver) : IRoleRightsEditor
{
    public async Task<string?> SetRightsAsync(string roleId, IReadOnlyCollection<string> rights, string actingUserName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rights);
        if (await roles.GetAsync(roleId, cancellationToken).ConfigureAwait(false) is not { } role)
        {
            return null;
        }

        var unknown = rights.Where(name => !Rights.IsKnown(name)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"Unknown right: {string.Join(", ", unknown)}.");
        }

        var chosen = rights.ToHashSet(StringComparer.Ordinal);
        var stored = string.Join(',', Rights.Catalog.Select(r => r.Name).Where(chosen.Contains));
        if (string.Equals(role.GetValue("Rights") as string, stored, StringComparison.Ordinal))
        {
            return stored;
        }

        await RefuseLockOutAsync(roleId, stored, actingUserName, cancellationToken).ConfigureAwait(false);
        role.Set("Rights", Guid.NewGuid().ToString(), stored);
        await roles.UpdateAsync(role, cancellationToken).ConfigureAwait(false);
        return stored;
    }

    private async Task RefuseLockOutAsync(string roleId, string stored, string actingUserName, CancellationToken cancellationToken)
    {
        static bool Manages(IReadOnlySet<string> held) => held.Contains(Rights.UsersManage) && held.Contains(Rights.RolesManage);

        var holdsNow = Manages(await resolver.ResolveAsync(actingUserName, [], cancellationToken).ConfigureAwait(false));
        var holdsAfter = Manages(await resolver.ResolveIfRoleAsync(actingUserName, roleId, stored, cancellationToken).ConfigureAwait(false));
        if (holdsNow && !holdsAfter)
        {
            throw new BusinessRuleException("You cannot take away your own ability to manage users and roles. Ask another administrator to make this change.");
        }

        var anyoneNow = false;
        foreach (var user in await users.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (user.GetValue("UserName") is not string name || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            anyoneNow |= Manages(await resolver.ResolveAsync(name, [], cancellationToken).ConfigureAwait(false));
            if (Manages(await resolver.ResolveIfRoleAsync(name, roleId, stored, cancellationToken).ConfigureAwait(false)))
            {
                return;
            }
        }

        // When the data names nobody today (the tenant is run by the sign-in role alone) there is nothing to protect yet.
        if (anyoneNow)
        {
            throw new BusinessRuleException("This change would leave nobody able to manage users and roles, so it was not made.");
        }
    }
}
