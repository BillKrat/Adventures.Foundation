namespace Adventures.Data.NQuad;

/// <summary>
/// Which tenant's graph the current request reads and writes (decision 2026-10, option C: a graph per tenant). <see cref="TenantKey"/> is
/// the tenant's id, never its domain; null means the primary tenant, whose data stays in today's graphs untouched.
/// </summary>
public interface IGraphScope
{
    string? TenantKey { get; }
}

/// <summary>The graph rules in one place, so the wall is not repeated in every query.</summary>
public static class GraphScopes
{
    /// <summary>Separates a registered graph from a tenant's: <c>{graph}/tenant/{key}</c>.</summary>
    public const string TenantSegment = "/tenant/";

    /// <summary>The graph a tenant writes to: the registered graph itself for the primary tenant (null key), else a graph under it named by the tenant id.</summary>
    public static string GraphFor(string registeredGraph, string? tenantKey) =>
        tenantKey is null ? registeredGraph : registeredGraph + TenantSegment + tenantKey;

    /// <summary>A tenant key becomes part of an IRI, so only letters, digits, '-' and '_' are allowed: no key can climb out of its graph.</summary>
    public static string? Validate(string? tenantKey)
    {
        if (tenantKey is null)
        {
            return null;
        }

        if (tenantKey.Length == 0 || !tenantKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("A tenant key may contain only letters, digits, '-' and '_'.", nameof(tenantKey));
        }

        return tenantKey;
    }

    /// <summary>
    /// Whether a stored quad belongs to the tenant. The primary tenant sees everything that is not under another tenant's graph (live data was
    /// seeded and imported into graphs the registration does not always name exactly); any other tenant sees only its own graph.
    /// </summary>
    public static bool Visible(NQuad quad, string registeredGraph, string? tenantKey) =>
        tenantKey is null
            ? quad.Graph is null || !quad.Graph.StartsWith(registeredGraph + TenantSegment, StringComparison.Ordinal)
            : string.Equals(quad.Graph, GraphFor(registeredGraph, tenantKey), StringComparison.Ordinal);
}
