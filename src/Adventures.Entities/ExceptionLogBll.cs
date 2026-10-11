using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IExceptionLogBll"/> - see that interface for the contract and rationale.</summary>
public sealed class ExceptionLogBll(
    IEntityRepository<ExceptionLogEntry> repository,
    EntitySchemaOf<ExceptionLogEntry> schema,
    ILogger<ExceptionLogBll>? logger = null)
    : EntityBll<ExceptionLogEntry>(repository), IExceptionLogBll
{
    /// <summary>A stack trace can be very long; keep each stored text bounded so one runaway exception cannot bloat the store.</summary>
    private const int MaxTextLength = 16_000;

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<string?> RecordAsync(Exception exception, string? request, string? userName, CancellationToken cancellationToken = default)
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

            await CreateAsync(entry, cancellationToken).ConfigureAwait(false);
            return id;
        }
        catch (Exception loggingFailure)
        {
            _logger.LogError(loggingFailure, "Could not record exception {ExceptionType} in the exception log", exception.GetType().Name);
            return null;
        }
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Count;

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
        text.Length <= MaxTextLength ? text : text[..MaxTextLength] + " ...[truncated]";
}
