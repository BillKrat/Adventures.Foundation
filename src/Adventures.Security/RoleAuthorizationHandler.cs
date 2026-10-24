// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Adventures.Security;

/// <summary>
/// Succeeds when the current principal has a "roles" claim whose space-delimited value contains
/// <see cref="RoleRequirement.RequiredRole"/> - the shape <c>UserAccountService</c> issues
/// (<see cref="WellKnownClaims.Roles"/>), the role counterpart to <see cref="ScopeAuthorizationHandler"/>.
/// </summary>
public sealed class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        // ASP.NET Core's JWT bearer handler remaps an incoming "roles" claim to ClaimTypes.Role by default, so a
        // token issued with WellKnownClaims.Roles reaches this handler as ClaimTypes.Role; accept both spellings.
        var roles = context.User.FindAll(claim => claim.Type is WellKnownClaims.Roles or ClaimTypes.Role)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (roles.Contains(requirement.RequiredRole, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
