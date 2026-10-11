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
        var roles = context.User.FindAll(WellKnownClaims.Roles)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (roles.Contains(requirement.RequiredRole, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
