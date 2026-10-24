// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Microsoft.AspNetCore.Authorization;

namespace Adventures.Security;

/// <summary>Authorization requirement satisfied when the current principal's "scope" claim contains the required scope.</summary>
public sealed class ScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = requiredScope;
}
