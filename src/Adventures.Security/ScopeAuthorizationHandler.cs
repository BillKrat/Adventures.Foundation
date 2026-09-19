using Microsoft.AspNetCore.Authorization;

namespace Adventures.Security;

/// <summary>
/// Succeeds when the current principal has a "scope" claim whose space-delimited value contains
/// the <see cref="ScopeRequirement.RequiredScope"/>.
/// </summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var scopeValues = context.User.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (scopeValues.Contains(requirement.RequiredScope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
