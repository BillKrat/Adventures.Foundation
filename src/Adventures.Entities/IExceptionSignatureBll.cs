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
}
