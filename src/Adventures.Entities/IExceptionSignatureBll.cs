// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Diagnostics;

namespace Adventures.Entities;

/// <summary>
/// Business layer for exception signatures: the one place that decides whether an exception is a failure already known
/// (reuse its signature, count another occurrence) or a new kind (create a signature).
/// </summary>
public interface IExceptionSignatureBll : IEntityBll<ExceptionSignatureEntry>
{
    /// <summary>
    /// Finds the signature of <paramref name="exception"/> or creates it, stamps its last-seen time and bumps its occurrence
    /// count, and returns the signature's entity id. Throws if the store is unavailable; the caller decides what that means.
    /// </summary>
    Task<string> RegisterOccurrenceAsync(Exception exception, CancellationToken cancellationToken = default);

    /// <summary>Every known signature, most recently seen first.</summary>
    Task<IReadOnlyList<ExceptionSignatureSummary>> ListSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the triage status an administrator chose (<see cref="ExceptionSignatureStatus.Acknowledged"/>, <see cref="ExceptionSignatureStatus.Fixed"/>
    /// or back to <see cref="ExceptionSignatureStatus.New"/>). Returns false when the signature does not exist; throws
    /// <see cref="Adventures.Common.Exceptions.InputValidationException"/> for a status that may not be set by hand.
    /// </summary>
    Task<bool> SetStatusAsync(string signatureId, string status, CancellationToken cancellationToken = default);
}

/// <summary>The life of a kind of failure: New, then Acknowledged (seen, being handled) or Fixed; a Fixed kind that occurs again becomes Regressed.</summary>
public static class ExceptionSignatureStatus
{
    public const string New = "New";
    public const string Acknowledged = "Acknowledged";
    public const string Fixed = "Fixed";
    public const string Regressed = "Regressed";

    /// <summary>What an administrator may choose; Regressed is only ever set by the system.</summary>
    public static readonly IReadOnlyList<string> Settable = [New, Acknowledged, Fixed];
}
