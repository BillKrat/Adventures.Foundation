using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.NQuad;

/// <summary>
/// <see cref="IUserMembership"/> read straight from the N-Quad store, using the registrations the host already made: the user's <c>TenantRoles</c> and <c>OrganizationRoles</c>
/// relations (the user points at each assignment), the assignments' <c>Tenant</c> and <c>Organization</c> fields and the organisations' <c>Tenant</c> field. The identity types
/// are shared by every tenant (not tenant-scoped), so the answer covers everybody. Worked out once per instance (one per request).
/// </summary>
public sealed class NQuadUserMembership(INQuadStore store, IEnumerable<EntityTypeRegistration> types) : IUserMembership
{
    private const string TenantRolesRelation = "TenantRoles";
    private const string OrganisationRolesRelation = "OrganizationRoles";

    private readonly Dictionary<string, EntityTypeRegistration> _bySchema =
        types.GroupBy(t => t.Schema.SchemaIri, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private Dictionary<string, HashSet<string>>? _tenantsByUser;

    public async Task<IReadOnlySet<string>> MemberIdsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var all = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(pair => pair.Value.Contains(tenantId)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<string>> TenantIdsOfAsync(string userId, CancellationToken cancellationToken = default)
    {
        var all = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return all.TryGetValue(userId, out var tenants) ? tenants : new HashSet<string>(StringComparer.Ordinal);
    }

    public async Task AddMemberAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if ((await TenantIdsOfAsync(userId, cancellationToken).ConfigureAwait(false)).Contains(tenantId))
        {
            return;
        }

        var user = Registration(EntityConstants.Schema.UserIri);
        var assignment = Registration(EntityConstants.Identity.TenantRoleSchemaIri);
        var relation = RelationOf(user, TenantRolesRelation);
        var id = Guid.NewGuid().ToString();
        var record = new DynamicRecord(assignment.Schema);
        record.Set(DynamicEntity.EntityIdPropertyName, id, id);
        record.Set("Tenant", Guid.NewGuid().ToString(), tenantId);
        await assignment.CreateRepository(store, scope: null).CreateAsync(record, cancellationToken).ConfigureAwait(false);
        await store.InsertAsync(new NQuad(Guid.NewGuid(), user.BaseIri + userId, relation.ViaPredicate, assignment.BaseIri + id, assignment.Graph), cancellationToken).ConfigureAwait(false);
        _tenantsByUser = null;
    }

    public async Task<bool> OrganisationInTenantAsync(string organisationId, string tenantId, CancellationToken cancellationToken = default)
    {
        var organisations = await OrganisationTenantsAsync(cancellationToken).ConfigureAwait(false);
        return organisations.TryGetValue(organisationId, out var tenant) && string.Equals(tenant, tenantId, StringComparison.Ordinal);
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_tenantsByUser is not null)
        {
            return _tenantsByUser;
        }

        var user = Registration(EntityConstants.Schema.UserIri);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var tenantAssignments = Registration(EntityConstants.Identity.TenantRoleSchemaIri);
        var tenantOf = (await tenantAssignments.CreateRepository(store, scope: null).ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(a => a.EntityId is not null && a.GetValue("Tenant") is string)
            .ToDictionary(a => a.EntityId!.Value, a => (string)a.GetValue("Tenant")!, StringComparer.Ordinal);
        await AddLinksAsync(user, tenantAssignments, RelationOf(user, TenantRolesRelation), tenantOf, result, cancellationToken).ConfigureAwait(false);

        var organisationAssignments = Registration(EntityConstants.Identity.OrganizationRoleSchemaIri);
        var organisations = await OrganisationTenantsAsync(cancellationToken).ConfigureAwait(false);
        var organisationTenantOf = (await organisationAssignments.CreateRepository(store, scope: null).ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(a => a.EntityId is not null && a.GetValue("Organization") is string org && organisations.ContainsKey(org))
            .ToDictionary(a => a.EntityId!.Value, a => organisations[(string)a.GetValue("Organization")!]!, StringComparer.Ordinal);
        await AddLinksAsync(user, organisationAssignments, RelationOf(user, OrganisationRolesRelation), organisationTenantOf, result, cancellationToken).ConfigureAwait(false);

        return _tenantsByUser = result;
    }

    private async Task<Dictionary<string, string?>> OrganisationTenantsAsync(CancellationToken cancellationToken) =>
        (await Registration(EntityConstants.Identity.OrganizationSchemaIri).CreateRepository(store, scope: null).ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(o => o.EntityId is not null)
            .ToDictionary(o => o.EntityId!.Value, o => o.GetValue("Tenant") as string, StringComparer.Ordinal);

    /// <summary>The user points at each assignment (the relation's predicate); an assignment counts for the tenant it names.</summary>
    private async Task AddLinksAsync(
        EntityTypeRegistration user, EntityTypeRegistration assignment, EntityRelationDefinition relation,
        Dictionary<string, string> tenantOfAssignment, Dictionary<string, HashSet<string>> result, CancellationToken cancellationToken)
    {
        foreach (var link in await store.QueryAsync(predicate: relation.ViaPredicate, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!link.Subject.StartsWith(user.BaseIri, StringComparison.Ordinal) || !link.Object.StartsWith(assignment.BaseIri, StringComparison.Ordinal)
                || !tenantOfAssignment.TryGetValue(link.Object[assignment.BaseIri.Length..], out var tenant))
            {
                continue;
            }

            var userId = link.Subject[user.BaseIri.Length..];
            if (!result.TryGetValue(userId, out var tenants))
            {
                result[userId] = tenants = new HashSet<string>(StringComparer.Ordinal);
            }

            tenants.Add(tenant);
        }
    }

    private EntityTypeRegistration Registration(string schemaIri) =>
        _bySchema.TryGetValue(schemaIri, out var registration) ? registration : throw new InvalidOperationException($"'{schemaIri}' is not registered.");

    private static EntityRelationDefinition RelationOf(EntityTypeRegistration type, string name) =>
        type.Schema.Relations.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"'{type.Schema.SchemaIri}' declares no relation '{name}'.");
}

public static class NQuadUserMembershipServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IUserMembership"/> over the N-Quad store (per request).</summary>
    public static IServiceCollection AddNQuadUserMembership(this IServiceCollection services) =>
        services.AddScoped<IUserMembership, NQuadUserMembership>();
}
