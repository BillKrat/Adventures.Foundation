using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.Identity;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Makes a new site on a domain: its sign-in account <c>Admin</c> with the default password (forced to change at the first sign-in), the Admin person, the tenant and the
/// person's membership. Used for the first site of a fresh install and for a new site chosen on the setup page. It never gives anyone the platform switch.
/// </summary>
public interface ISiteProvisioner
{
    /// <summary>
    /// Creates the site. Throws <see cref="BusinessRuleException"/> (nothing is created) for a domain that is not a real name or that another tenant already reaches.
    /// </summary>
    Task<TenantHostInfo> CreateAsync(string domain, string name, CancellationToken cancellationToken = default);
}

/// <summary>For a host with no sign-in store (the Legacy developer mode): no site can be made.</summary>
public sealed class NoSiteProvisioner : ISiteProvisioner
{
    public Task<TenantHostInfo> CreateAsync(string domain, string name, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("This install has no sign-in store, so a new site cannot be made.");
}

public sealed class SiteProvisioner(
    ITenantAliasEditor tenants, ITenantCreator creator, IAccountCreator accounts, IUserBll people, IUserMembership membership, EntitySchemaOf<User> userSchema) : ISiteProvisioner
{
    public const string AdminUserName = "Admin";
    public const string DefaultPassword = "Admin";

    public async Task<TenantHostInfo> CreateAsync(string domain, string name, CancellationToken cancellationToken = default)
    {
        var site = TenantHosts.Normalize(domain);
        if (!TenantHosts.IsRealName(site))
        {
            throw new BusinessRuleException("The domain must be a real site name such as photos.example.org (not an address, localhost, or a name with no dot).");
        }

        // before anything is written: a name another tenant already reaches (or sits under, or is above) must not leave an account behind
        foreach (var other in await tenants.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var known in other.Aliases.Prepend(TenantHosts.Normalize(other.Domain)))
            {
                if (site == known || site.EndsWith("." + known, StringComparison.Ordinal) || known.EndsWith("." + site, StringComparison.Ordinal))
                {
                    throw new BusinessRuleException($"{site} already reaches {other.Name}. A host name can belong to only one tenant.");
                }
            }
        }

        await accounts.CreateIfMissingAsync(site, AdminUserName, DefaultPassword, ["TenantAdmin"], "admin@" + site, "Administrator", cancellationToken).ConfigureAwait(false);

        var person = new User(userSchema.Schema);
        // the repository refuses an entity with no Id (the forms and presenters assign one for a person made on screen; this path has no presenter)
        var personId = Guid.NewGuid().ToString();
        person.Set(DynamicEntity.EntityIdPropertyName, Guid.NewGuid().ToString(), personId);
        person.Set("UserName", Guid.NewGuid().ToString(), AdminUserName);
        person.Set("First", Guid.NewGuid().ToString(), "Administrator");
        var created = await people.CreateInTenantAsync(person, new HashSet<string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);

        var tenant = await creator.CreateAsync(name, site, cancellationToken).ConfigureAwait(false);
        await membership.AddMemberAsync(created.EntityId!.Value, tenant.Id, cancellationToken).ConfigureAwait(false);
        return tenant;
    }
}
