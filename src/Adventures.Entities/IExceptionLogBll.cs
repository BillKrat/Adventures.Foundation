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
    /// stored - in which case the failure itself is only written to the application log.
    /// </summary>
    Task<string?> RecordAsync(Exception exception, string? request, string? userName, CancellationToken cancellationToken = default);

    /// <summary>Number of entries not yet purged.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Tombstones every active entry. Returns how many were purged.</summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);
}
