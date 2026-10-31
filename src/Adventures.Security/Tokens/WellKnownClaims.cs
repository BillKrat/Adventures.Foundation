// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Security;

/// <summary>Claim names and role names shared by the issuer (Adventures.Identity) and the authorization handlers.</summary>
public static class WellKnownClaims
{
    /// <summary>Space-delimited list of the caller's role names.</summary>
    public const string Roles = "roles";

    /// <summary>Tenant-wide administrator: full user management (seed account "Admin").</summary>
    public const string TenantAdminRole = "TenantAdmin";

    /// <summary>Name of the policy <c>AddRoleAuthorization(TenantAdminRole)</c> registers - a constant so it can be used in an <c>[Authorize]</c> attribute.</summary>
    public const string TenantAdminPolicy = "Role:" + TenantAdminRole;
}
