namespace Adventures.Entities;

/// <summary>Works out what a person may do: the rights of the roles assigned to them. The host asks it; nothing here changes data.</summary>
public interface IRightsResolver
{
    /// <summary>
    /// The rights of the person with this user name: the union of the rights of every role assigned to them (tenant and organisation
    /// assignments). A person who signed in with the legacy <c>TenantAdmin</c> role has every right, so existing administrators keep working.
    /// A person who is unknown, or has no role, has none.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveAsync(string userName, IEnumerable<string> signInRoles, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the person from the data alone would be allowed (no sign-in roles) if the role <paramref name="roleId"/> carried <paramref name="roleRights"/> instead of what it holds now.
    /// Nothing is saved; the role editor uses it to refuse a change that would lock administrators out before it is made.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveIfRoleAsync(string userName, string roleId, string roleRights, CancellationToken cancellationToken = default);
}

/// <summary>Resolves rights from the role assignments edited on Admin > Users and the <c>Rights</c> of each role.</summary>
public sealed class RightsResolver(
    IUserBll users, IEntityBll<Role> roles, IEntityRelationProvider relations, EntitySchemaOf<User> userSchema) : IRightsResolver
{
    public const string LegacyAdministratorRole = "TenantAdmin";

    public Task<IReadOnlySet<string>> ResolveAsync(string userName, IEnumerable<string> signInRoles, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(userName, signInRoles, null, null, cancellationToken);

    public Task<IReadOnlySet<string>> ResolveIfRoleAsync(string userName, string roleId, string roleRights, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(userName, [], roleId, roleRights, cancellationToken);

    private async Task<IReadOnlySet<string>> ResolveCoreAsync(
        string userName, IEnumerable<string> signInRoles, string? overrideRoleId, string? overrideRights, CancellationToken cancellationToken)
    {
        if (signInRoles.Contains(LegacyAdministratorRole, StringComparer.Ordinal))
        {
            return Rights.AllNames;
        }

        if (string.IsNullOrWhiteSpace(userName) || await users.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false) is not { EntityId: { } id })
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var granted = new HashSet<string>(StringComparer.Ordinal);
        var assignments = await relations.GetRelationsAsync(userSchema.Schema, id.Value, editable: false, cancellationToken).ConfigureAwait(false);
        var roleIds = assignments
            .SelectMany(relation => relation.Grid.Rows)
            .Select(row => row.Values.GetValueOrDefault("Role"))
            .Where(roleId => !string.IsNullOrEmpty(roleId))
            .Distinct(StringComparer.Ordinal);
        foreach (var roleId in roleIds)
        {
            if (await roles.GetAsync(roleId!, cancellationToken).ConfigureAwait(false) is { } role)
            {
                granted.UnionWith(Rights.Parse(roleId == overrideRoleId ? overrideRights : role.GetValue("Rights") as string));
            }
        }

        return granted;
    }
}
