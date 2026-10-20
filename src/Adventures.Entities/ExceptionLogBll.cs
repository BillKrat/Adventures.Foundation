using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IExceptionLogBll"/> - see that interface for the contract and rationale.</summary>
public sealed class ExceptionLogBll(
    IEntityRepository<ExceptionLogEntry> repository,
    EntitySchemaOf<ExceptionLogEntry> schema,
    ILogger<ExceptionLogBll>? logger = null,
    IExceptionSignatureBll? signatures = null)
    : EntityBll<ExceptionLogEntry>(repository), IExceptionLogBll
{
    /// <summary>Exceptions are rare and their whole payload is what triage needs, so the cap is far above any real stack trace (250,000 characters); it only stops a runaway from bloating the store, and a cut says how much was removed.</summary>
    private const int MaxTextLength = 250_000;

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<string?> RecordAsync(
        Exception exception, string? request, string? userName, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var id = Guid.NewGuid().ToString();
            var entry = new ExceptionLogEntry(schema.Schema);
            entry.Set(DynamicEntity.EntityIdPropertyName, id, id);
            entry.Set("OccurredAt", NewId(), DateTimeOffset.UtcNow);
            entry.Set("ExceptionType", NewId(), exception.GetType().FullName ?? exception.GetType().Name);
            entry.Set("Message", NewId(), Truncate(exception.Message));
            entry.Set("StackTrace", NewId(), Truncate(exception.ToString()));
            if (!string.IsNullOrWhiteSpace(request))
            {
                entry.Set("Request", NewId(), Truncate(request));
            }

            if (!string.IsNullOrWhiteSpace(userName))
            {
                entry.Set("UserName", NewId(), Truncate(userName));
            }

            if (!string.IsNullOrWhiteSpace(correlationId) && schema.Schema.Fields.ContainsKey("CorrelationId"))
            {
                entry.Set("CorrelationId", NewId(), Truncate(correlationId));
            }

            if (signatures is not null && schema.Schema.Fields.ContainsKey("SignatureId"))
            {
                // Grouping is a bonus: if it fails the occurrence is still recorded, without a signature.
                try
                {
                    entry.Set("SignatureId", NewId(), await signatures.RegisterOccurrenceAsync(exception, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception signatureFailure)
                {
                    _logger.LogWarning(signatureFailure, "Could not register the signature of {ExceptionType}", exception.GetType().Name);
                }
            }

            await CreateAsync(entry, cancellationToken).ConfigureAwait(false);
            return id;
        }
        catch (Exception loggingFailure)
        {
            _logger.LogError(loggingFailure, "Could not record exception {ExceptionType} in the exception log", exception.GetType().Name);
            return null;
        }
    }

    /// <summary>When each signature was last recorded through <see cref="RecordThrottledAsync"/>, per process.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> LastThrottled = new();

    public Task<string?> RecordThrottledAsync(
        Exception exception, string? request, string? userName, string? correlationId, TimeSpan window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var hash = ExceptionSignature.From(exception).Hash;
        var now = DateTimeOffset.UtcNow;
        var allowed = false;
        LastThrottled.AddOrUpdate(
            hash,
            _ =>
            {
                allowed = true;
                return now;
            },
            (_, last) =>
            {
                allowed = now - last >= window;
                return allowed ? now : last;
            });

        return allowed
            ? RecordAsync(exception, request, userName, correlationId, cancellationToken)
            : Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<ExceptionLogEntry>> ListIncludingPurgedAsync(CancellationToken cancellationToken = default) =>
        Repository.ListAsync(includeDeleted: true, cancellationToken);

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        Repository.CountAsync(includeDeleted: false, cancellationToken);

    public Task<int> CountIncludingPurgedAsync(CancellationToken cancellationToken = default) =>
        Repository.CountAsync(includeDeleted: true, cancellationToken);

    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var ids = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(entry => entry.EntityId?.Value)
            .OfType<string>()
            .ToArray();

        return await DeleteManyAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    private static string NewId() => Guid.NewGuid().ToString();

    private static string Truncate(string text) =>
        text.Length <= MaxTextLength ? text : text[..MaxTextLength] + $" ...[truncated {text.Length - MaxTextLength} chars]";
}
