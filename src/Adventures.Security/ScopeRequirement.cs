using Microsoft.AspNetCore.Authorization;

namespace Adventures.Security;

/// <summary>Authorization requirement satisfied when the current principal's "scope" claim contains the required scope.</summary>
public sealed class ScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = requiredScope;
}
