using Microsoft.AspNetCore.Authorization;

namespace Adventures.Security;

/// <summary>Requires the principal to hold <paramref name="RequiredRole"/> in its "roles" claim.</summary>
public sealed record RoleRequirement(string RequiredRole) : IAuthorizationRequirement;
