// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Common.Exceptions;

/// <summary>
/// A business rule refused the request (e.g. "you cannot delete your own account"). The message is
/// written by the rule's author for the end user, so a host may show it as-is (HTTP 409). Everything
/// else that goes wrong - a missing schema, a store failure - is a system exception: it must be logged
/// and shielded, never echoed to the caller (AppArch: exception shielding).
/// </summary>
public class BusinessRuleException(string message) : Exception(message);
