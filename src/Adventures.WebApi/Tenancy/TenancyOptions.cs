namespace Adventures.WebApi;

/// <summary>What the host does with a request whose host name matches no tenant.</summary>
public enum TenancyEnforcement
{
    /// <summary>Serve it silently (the tenant is still resolved for whoever wants it).</summary>
    Off,

    /// <summary>Serve it, but log a warning (once per host per cache window): the safe first step when switching tenancy on for a live site.</summary>
    Report,

    /// <summary>Refuse it with 404. Infrastructure paths (<see cref="TenancyOptions.ExemptPaths"/>) are never refused.</summary>
    Enforce,
}

/// <summary>Settings of the <c>Tenancy</c> section.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Default <see cref="TenancyEnforcement.Report"/>: nothing that works today stops working until the host sets <c>Tenancy:Enforcement</c> to <c>Enforce</c>.</summary>
    public TenancyEnforcement Enforcement { get; set; } = TenancyEnforcement.Report;

    /// <summary>
    /// The id of the primary tenant: the one whose content stays in today's graphs. Every other tenant gets its own graph (T2). Unset means tenant
    /// scoping is off and all content is shared, which is today's single-tenant behaviour; set it before a second tenant holds any content.
    /// </summary>
    public string? PrimaryTenantId { get; set; }

    /// <summary>A development stand-in domain for a host that matches no tenant (for example localhost). Used only when <see cref="AllowFallback"/> is true.</summary>
    public string? FallbackDomain { get; set; }

    /// <summary>Set by the host, never by configuration: true only in the Development environment, so a deployed site cannot fall into a tenant by accident.</summary>
    public bool AllowFallback { get; set; }

    /// <summary>How long the tenant answer for one host name is kept, so not every request reads the tenant store. A new tenant takes at most this long to be recognised. 0 turns the cache off.</summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>Paths never refused, even for an unknown host: the domain root (the hosting provider's certificate request needs a 200 there) and the public health check (the deploy smoke test).</summary>
    public IReadOnlyList<string> ExemptPaths { get; } = ["/", "/api/health"];
}
