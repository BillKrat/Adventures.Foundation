namespace Adventures.Data.NQuad;

/// <summary>
/// Which tenant's graph the current request reads and writes (decision 2026-10, option C: a graph per tenant; 2026-11-07: every tenant, no exceptions).
/// <see cref="TenantKey"/> is the tenant's id, never its domain. It throws <see cref="Adventures.Entities.TenantRequiredException"/> when the request has no tenant.
/// </summary>
public interface IGraphScope
{
    string TenantKey { get; }
}

/// <summary>The graph rules in one place, so the wall is not repeated in every query.</summary>
public static class GraphScopes
{
    /// <summary>Separates a registered graph from a tenant's: <c>{graph}/tenant/{key}</c>.</summary>
    public const string TenantSegment = "/tenant/";

    /// <summary>The graph a tenant reads and writes: a graph under the registered one, named by the tenant id.</summary>
    public static string GraphFor(string registeredGraph, string tenantKey) => registeredGraph + TenantSegment + Validate(tenantKey);

    /// <summary>A tenant key becomes part of an IRI, so only letters, digits, '-' and '_' are allowed: no key can climb out of its graph.</summary>
    public static string Validate(string tenantKey)
    {
        if (string.IsNullOrEmpty(tenantKey) || !tenantKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("A tenant key may contain only letters, digits, '-' and '_'.", nameof(tenantKey));
        }

        return tenantKey;
    }
}
