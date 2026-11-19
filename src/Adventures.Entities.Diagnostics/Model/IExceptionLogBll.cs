// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// Business layer for the exception log. <see cref="RecordAsync"/> is the entry point the host's global handler calls;
/// it never throws (a failure to log must not mask the exception being logged). A log entry is an ordinary entity, so
/// "purging" is the same tombstone every other delete is: nothing is physically removed and the history stays.
/// </summary>
public interface IExceptionLogBll : IEntityBll<ExceptionLogEntry>
{
    /// <summary>
    /// Records <paramref name="exception"/> (type, message, full stack trace including inner exceptions) with the request
    /// and user it happened for. Returns the new entry's id (a reference to show the user), or null if it could not be
    /// stored - in which case the failure itself is only written to the application log. When the schema has the fields
    /// (seed 003) the entry also carries the request's <paramref name="correlationId"/> and the id of the exception's
    /// signature (the same failure seen again shares one signature).
    /// </summary>
    Task<string?> RecordAsync(Exception exception, string? request, string? userName, string? correlationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="RecordAsync"/> for failures that can repeat without end (a dependency probe, the browser reporting the
    /// same error again): records at most one entry per signature per <paramref name="window"/> in this process, so the
    /// administrator sees the problem without a flood. Returns null when suppressed (the caller still logs the warning).
    /// </summary>
    Task<string?> RecordThrottledAsync(
        Exception exception, string? request, string? userName, string? correlationId, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>Every entry ever recorded, purged ones included (a purge is a soft delete, so the history stays readable).</summary>
    Task<IReadOnlyList<ExceptionLogEntry>> ListIncludingPurgedAsync(CancellationToken cancellationToken = default);

    /// <summary>Number of entries not yet purged.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>How many entries exist, purged ones included - counted, not loaded.</summary>
    Task<int> CountIncludingPurgedAsync(CancellationToken cancellationToken = default);

    /// <summary>Tombstones every active entry. Returns how many were purged.</summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);
}
