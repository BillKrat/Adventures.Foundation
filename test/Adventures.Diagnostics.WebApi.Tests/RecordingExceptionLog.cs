// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;

namespace Adventures.Diagnostics.WebApi.Tests;

/// <summary>Test double for the exception log: remembers what was recorded and returns a fixed entry id.</summary>
internal sealed class RecordingExceptionLog(string? idToReturn) : IExceptionLogBll
{
    public List<(Exception Exception, string? Request, string? CorrelationId, TimeSpan Window)> Throttled { get; } = [];

    public string? LastUserName { get; private set; }

    public Task<string?> RecordThrottledAsync(
        Exception exception, string? request, string? userName, string? correlationId, TimeSpan window, CancellationToken cancellationToken = default)
    {
        LastUserName = userName;
        Throttled.Add((exception, request, correlationId, window));
        return Task.FromResult(idToReturn);
    }

    public List<(Exception Exception, string? Request, string? UserName)> Recorded { get; } = [];

    public string? LastCorrelationId { get; private set; }

    public Task<string?> RecordAsync(Exception exception, string? request, string? userName, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        LastCorrelationId = correlationId;
        Recorded.Add((exception, request, userName));
        return Task.FromResult(idToReturn);
    }

    public Task<IReadOnlyList<ExceptionLogEntry>> ListIncludingPurgedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<int> CountIncludingPurgedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<ExceptionLogEntry> CreateAsync(ExceptionLogEntry entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ExceptionLogEntry?> GetAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ExceptionLogEntry>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task UpdateAsync(ExceptionLogEntry entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
