using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>Creates a tenant on a domain from one place.</summary>
public interface ITenantCreator
{
    /// <summary>
    /// A new tenant with the given name and domain. Throws <see cref="BusinessRuleException"/> (nothing is created) for an empty or too long name, a domain that is not a real name
    /// (an address, localhost, no dot), or one another tenant already reaches (its domain or an alias, or a name above or under one).
    /// </summary>
    Task<TenantHostInfo> CreateAsync(string name, string domain, CancellationToken cancellationToken = default);
}

public sealed class TenantCreator(IEntityBll<Tenant> tenants, EntitySchemaOf<Tenant> schema) : ITenantCreator
{
    public const int MaxNameLength = 80;

    public async Task<TenantHostInfo> CreateAsync(string name, string domain, CancellationToken cancellationToken = default)
    {
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is 0 or > MaxNameLength || cleanName.Any(char.IsControl))
        {
            throw new BusinessRuleException($"Give the tenant a name of 1 to {MaxNameLength} characters.");
        }

        var host = TenantHosts.Normalize(domain);
        if (!TenantHosts.IsRealName(host))
        {
            throw new BusinessRuleException("The domain must be a real site name such as photos.example.org (not an address, localhost, or a name with no dot).");
        }

        foreach (var other in await tenants.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TenantAliasEditor.Reaches(other, host))
            {
                throw new BusinessRuleException($"{host} already reaches {other.GetValue("Name") as string}. A host name can belong to only one tenant.");
            }
        }

        var tenant = new Tenant(schema.Schema);
        tenant.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        tenant.Set("Name", Guid.NewGuid().ToString(), cleanName);
        tenant.Set("Domain", Guid.NewGuid().ToString(), host);
        var created = await tenants.CreateAsync(tenant, cancellationToken).ConfigureAwait(false);
        return new TenantHostInfo(created.EntityId!.Value, cleanName, host, []);
    }
}

/// <summary>
/// The first run of a fresh install: before anyone can sign in, the temporary address of the site must already reach a tenant. When the operator names a tenant
/// (by its domain), an unknown host becomes one of its aliases. It is refused for anything that is not a real name, for a host some tenant already reaches, and
/// beyond <see cref="TenantFirstRun.MaxAutomaticAliases"/> aliases on the tenant.
/// </summary>
public interface ITenantFirstRun
{
    /// <summary>True when the host was added as an alias of the tenant whose domain is <paramref name="targetDomain"/>; false (nothing changed) otherwise.</summary>
    Task<bool> TryAliasAsync(string host, string? targetDomain, CancellationToken cancellationToken = default);
}

public sealed class TenantFirstRun(ITenantAliasEditor aliases) : ITenantFirstRun
{
    public const int MaxAutomaticAliases = 10;

    public async Task<bool> TryAliasAsync(string host, string? targetDomain, CancellationToken cancellationToken = default)
    {
        var name = TenantHosts.Normalize(host);
        var target = TenantHosts.Normalize(targetDomain);
        if (target.Length == 0 || !TenantHosts.IsRealName(name))
        {
            return false;
        }

        var all = await aliases.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(t => TenantHosts.Normalize(t.Domain) == target) is not { } tenant
            || tenant.Aliases.Count >= MaxAutomaticAliases
            || all.Any(t => Names(t).Any(n => name == n || name.EndsWith("." + n, StringComparison.Ordinal) || n.EndsWith("." + name, StringComparison.Ordinal))))
        {
            return false;
        }

        try
        {
            return await aliases.AddAsync(tenant.Id, name, cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (BusinessRuleException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Names(TenantHostInfo tenant) => tenant.Aliases.Prepend(TenantHosts.Normalize(tenant.Domain));
}
