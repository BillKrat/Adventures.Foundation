// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Interfaces;

namespace Adventures.Common.Diagnostics;

/// <summary>One recorded exception, as a person or an MCP tool reads it.</summary>
public sealed record ExceptionOccurrence(
    string Id,
    DateTimeOffset? OccurredAt,
    string ExceptionType,
    string Message,
    string? Request,
    string? UserName,
    string? CorrelationId,
    string? SignatureId);

/// <summary>One kind of failure (an exception type, its message pattern and top frame) with how often and when it was seen.</summary>
public sealed record ExceptionSignatureSummary(
    string Id,
    string ExceptionType,
    string MessagePattern,
    string TopFrame,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    int OccurrenceCount,
    string Status);

/// <summary>Everything the store knows about one request: the exceptions it raised and the kinds of failure they belong to.</summary>
public sealed record CorrelationReport(
    string CorrelationId,
    IReadOnlyList<ExceptionOccurrence> Exceptions,
    IReadOnlyList<ExceptionSignatureSummary> Signatures);

/// <summary>
/// Read-only eyes into the running system: the one seam an MCP tool (or an admin screen) uses to ask "what went wrong
/// for this request" without knowing how exceptions are stored. Nothing here changes state. The host adds what only it
/// can read (its log files); this interface covers what is in the store.
/// </summary>
public interface IDiagnosticsReader : IBll
{
    /// <summary>The exceptions recorded under <paramref name="correlationId"/> and their signatures.</summary>
    Task<CorrelationReport> FindByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);

    /// <summary>The most recent active exceptions, newest first.</summary>
    Task<IReadOnlyList<ExceptionOccurrence>> ListRecentAsync(int max = 50, CancellationToken cancellationToken = default);

    /// <summary>Every known kind of failure with its count, most recently seen first.</summary>
    Task<IReadOnlyList<ExceptionSignatureSummary>> ListSignaturesAsync(CancellationToken cancellationToken = default);
}
