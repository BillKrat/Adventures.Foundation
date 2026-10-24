// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Common.Diagnostics;

/// <summary>
/// The rules of a correlation id that do not need a web request: the names it travels under and what counts as well formed.
/// The ASP.NET Core middleware that assigns the id (<c>Adventures.WebApi.CorrelationIdMiddleware</c>) and the readers of the
/// logs (<see cref="LogSearch"/>) both use this, so neither depends on the other.
/// </summary>
public static class CorrelationIds
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogPropertyName = "CorrelationId";
    public const string ActivityTagName = "correlation.id";
    public const int MaxLength = 64;

    /// <summary>Only letters, digits, '-' and '_' up to 64 characters: the id lands in logs and headers, so nothing else is trusted.</summary>
    public static bool IsWellFormed(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && candidate.Length <= MaxLength
        && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
