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

/// <summary>Whether a browser site's own name may choose the tenant when sites share one API address.</summary>
public enum TenancyOriginHint
{
    /// <summary>The default: only the host name the request reached decides the tenant.</summary>
    Off,

    /// <summary>
    /// A request whose host reaches a tenant may be answered for another tenant when the browser's <c>Origin</c> (or, for a plain read such as an image, the <c>Referer</c> site) is
    /// exactly a host name some tenant answers to. Never an unregistered name, never for the setup and recovery paths, and a token still has to belong to the tenant chosen.
    /// </summary>
    Registered,
}

/// <summary>Settings of the <c>Tenancy</c> section.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Default <see cref="TenancyEnforcement.Report"/>: nothing that works today stops working until the host sets <c>Tenancy:Enforcement</c> to <c>Enforce</c>.</summary>
    public TenancyEnforcement Enforcement { get; set; } = TenancyEnforcement.Report;

    /// <summary>A development stand-in domain for a host that matches no tenant (for example localhost). Used only when <see cref="AllowFallback"/> is true.</summary>
    public string? FallbackDomain { get; set; }

    /// <summary>Set by the host, never by configuration: true only in the Development environment, so a deployed site cannot fall into a tenant by accident.</summary>
    public bool AllowFallback { get; set; }

    /// <summary>
    /// The domain of the tenant that receives an unknown host as an alias (the install step that makes the site's temporary address work before anyone can sign in). Empty (the
    /// default) switches it off: an unknown host is then reported or refused. Turn it on for the first run, then clear it again.
    /// </summary>
    public string? AutoAliasToTenantDomain { get; set; }

    /// <summary>Default <see cref="TenancyOriginHint.Off"/>. Turn it on (<c>Tenancy:OriginHint</c> = <c>Registered</c>) for an installation whose web sites share one API address, so each site gets its own tenant.</summary>
    public TenancyOriginHint OriginHint { get; set; } = TenancyOriginHint.Off;

    /// <summary>How long the tenant answer for one host name is kept, so not every request reads the tenant store. A new tenant takes at most this long to be recognised. 0 turns the cache off.</summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>Paths never refused, even for an unknown host: the domain root (the hosting provider's certificate request needs a 200 there) and the public health check (the deploy smoke test).</summary>
    public IReadOnlyList<string> ExemptPaths { get; } = ["/", "/api/health"];

    /// <summary>
    /// Paths (and everything under them) never refused for an unknown host and never the trigger of a first site: the setup API is how an unknown host gets set up, and the recovery
    /// API (a code and a file on the host) is how an owner locked out of a site gets back in, including from the setup page.
    /// </summary>
    public IReadOnlyList<string> ExemptPrefixes { get; } = ["/api/setup", "/api/recovery"];
}
