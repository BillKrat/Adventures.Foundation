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
