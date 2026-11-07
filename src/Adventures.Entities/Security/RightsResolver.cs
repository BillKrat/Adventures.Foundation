namespace Adventures.Entities;

/// <summary>Works out what a person may do inside one tenant: the rights of the roles assigned to them there. The host asks it; nothing here changes data.</summary>
public interface IRightsResolver
{
    /// <summary>
    /// The rights of the person with this user name <b>in the tenant</b> <paramref name="tenantId"/>: the union of the rights of the roles assigned to them in that tenant (a tenant
    /// role assignment for it, or an organisation assignment for an organisation that belongs to it). A role held in another tenant gives nothing here. A person who signed in
    /// with the legacy <c>TenantAdmin</c> role has every tenant right (never the platform right), so existing administrators keep working; the caller passes that role only when
    /// the token belongs to this tenant. An unknown person, a person with no role here, or no tenant at all has none.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveAsync(string userName, IEnumerable<string> signInRoles, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the person from the data alone would be allowed (no sign-in roles, in any tenant: roles are defined for the whole platform) if the role <paramref name="roleId"/> carried
    /// <paramref name="roleRights"/> instead of what it holds now. Nothing is saved; the role editor uses it to refuse a change that would lock administrators out before it is made.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveIfRoleAsync(string userName, string roleId, string roleRights, CancellationToken cancellationToken = default);

    /// <summary>The union of a person's rights over every tenant they hold a role in (no sign-in roles). For the platform-wide role editor lock-out check only; never use it to allow a request.</summary>
    Task<IReadOnlySet<string>> ResolveAcrossTenantsAsync(string userName, CancellationToken cancellationToken = default);
}

/// <summary>Resolves rights from the role assignments edited on Admin > Users and the <c>Rights</c> of each role.</summary>
public sealed class RightsResolver(
    IUserBll users, IEntityBll<Role> roles, IEntityBll<Organization> organizations, IEntityRelationProvider relations, EntitySchemaOf<User> userSchema) : IRightsResolver
{
    public const string LegacyAdministratorRole = "TenantAdmin";

    public Task<IReadOnlySet<string>> ResolveAsync(string userName, IEnumerable<string> signInRoles, string tenantId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(tenantId)
            ? Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal))
            : ResolveCoreAsync(userName, signInRoles, tenantId, null, null, cancellationToken);

    public Task<IReadOnlySet<string>> ResolveAcrossTenantsAsync(string userName, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(userName, [], null, null, null, cancellationToken);

    public Task<IReadOnlySet<string>> ResolveIfRoleAsync(string userName, string roleId, string roleRights, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(userName, [], null, roleId, roleRights, cancellationToken);

    /// <param name="tenantId">The tenant whose assignments count; null means every tenant (only the platform-wide lock-out check uses that).</param>
    private async Task<IReadOnlySet<string>> ResolveCoreAsync(
        string userName, IEnumerable<string> signInRoles, string? tenantId, string? overrideRoleId, string? overrideRights, CancellationToken cancellationToken)
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
        var rows = assignments.SelectMany(relation => relation.Grid.Rows).ToList();
        var organisationTenants = tenantId is null || !rows.Any(row => row.Values.ContainsKey("Organization"))
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : (await organizations.ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(o => o.EntityId is not null)
                .ToDictionary(o => o.EntityId!.Value, o => o.GetValue("Tenant") as string, StringComparer.Ordinal);

        var roleIds = rows
            .Where(row => AppliesIn(row, tenantId, organisationTenants))
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

    /// <summary>A tenant assignment names its tenant; an organisation assignment counts through the tenant its organisation belongs to.</summary>
    private static bool AppliesIn(EntityDataModel row, string? tenantId, IReadOnlyDictionary<string, string?> organisationTenants)
    {
        if (tenantId is null)
        {
            return true;
        }

        if (row.Values.TryGetValue("Tenant", out var assignedTenant))
        {
            return string.Equals(assignedTenant, tenantId, StringComparison.Ordinal);
        }

        return row.Values.TryGetValue("Organization", out var organisation)
            && organisation is not null
            && organisationTenants.TryGetValue(organisation, out var belongsTo)
            && string.Equals(belongsTo, tenantId, StringComparison.Ordinal);
    }
}
