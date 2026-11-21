namespace Adventures.Entities;

/// <summary>Every host name the registered tenants answer to: each tenant's domain and its aliases. Read only; used to decide which browser sites may call the API.</summary>
public interface ITenantHostSource
{
    Task<IReadOnlyList<string>> AllNamesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads the names from the Tenant entities (the same ones <see cref="TenantResolver"/> matches a request against).</summary>
public sealed class TenantHostSource(IEntityBll<Tenant> tenants) : ITenantHostSource
{
    public async Task<IReadOnlyList<string>> AllNamesAsync(CancellationToken cancellationToken = default) =>
        (await tenants.ListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(TenantAliasEditor.Names)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
