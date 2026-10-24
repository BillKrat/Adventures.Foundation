// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// A failure that was written down when it happened and is reported to the exception log later (a crash during startup,
/// when the store may not have been reachable; a process-level failure). The original type and stack are kept as text and
/// shown where a live exception's would be; the signature groups repeats by the original type and message.
/// </summary>
public sealed class PersistedFailureException(string originalType, string message, string? originalStack)
    : Exception($"{originalType}: {message}")
{
    public string OriginalType { get; } = originalType;

    public override string? StackTrace => originalStack;
}
