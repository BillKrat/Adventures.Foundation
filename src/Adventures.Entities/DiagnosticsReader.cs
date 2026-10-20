using Adventures.Common.Diagnostics;

namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IDiagnosticsReader"/> over the exception log and the exception signatures.</summary>
public sealed class DiagnosticsReader(IExceptionLogBll log, IExceptionSignatureBll signatures) : IDiagnosticsReader
{
    public async Task<CorrelationReport> FindByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var matches = (await log.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(entry => string.Equals(entry.GetValue("CorrelationId") as string, correlationId, StringComparison.Ordinal))
            .Select(ToOccurrence)
            .OrderBy(occurrence => occurrence.OccurredAt)
            .ToList();

        var signatureIds = matches.Select(m => m.SignatureId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var kinds = (await signatures.ListSummariesAsync(cancellationToken).ConfigureAwait(false))
            .Where(kind => signatureIds.Contains(kind.Id))
            .ToList();

        return new CorrelationReport(correlationId, matches, kinds);
    }

    public async Task<IReadOnlyList<ExceptionOccurrence>> ListRecentAsync(int max = 50, CancellationToken cancellationToken = default) =>
        (await log.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(ToOccurrence)
            .OrderByDescending(occurrence => occurrence.OccurredAt)
            .Take(Math.Clamp(max, 1, 500))
            .ToList();

    public Task<IReadOnlyList<ExceptionSignatureSummary>> ListSignaturesAsync(CancellationToken cancellationToken = default) =>
        signatures.ListSummariesAsync(cancellationToken);

    private static ExceptionOccurrence ToOccurrence(ExceptionLogEntry entry) => new(
        entry.EntityId!.Value,
        entry.GetValue("OccurredAt") as DateTimeOffset?,
        entry.GetValue("ExceptionType") as string ?? string.Empty,
        entry.GetValue("Message") as string ?? string.Empty,
        entry.GetValue("Request") as string,
        entry.GetValue("UserName") as string,
        entry.GetValue("CorrelationId") as string,
        entry.GetValue("SignatureId") as string);
}
