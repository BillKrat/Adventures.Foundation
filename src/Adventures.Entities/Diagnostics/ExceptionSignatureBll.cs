// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Diagnostics;

namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IExceptionSignatureBll"/> - see that interface for the contract.</summary>
public sealed class ExceptionSignatureBll(
    IEntityDal<ExceptionSignatureEntry> repository,
    EntitySchemaOf<ExceptionSignatureEntry> schema)
    : EntityBll<ExceptionSignatureEntry>(repository), IExceptionSignatureBll
{
    public const string NewStatus = ExceptionSignatureStatus.New;

    public async Task<string> RegisterOccurrenceAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var signature = ExceptionSignature.From(exception);
        var now = DateTimeOffset.UtcNow;

        // Signatures are few (one per kind of failure), so a scan by hash is cheap; the Hash predicate is the lookup key.
        var known = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entry => string.Equals(entry.GetValue("Hash") as string, signature.Hash, StringComparison.Ordinal));

        if (known is not null)
        {
            var count = known.GetValue("OccurrenceCount") is int current ? current : 0;
            known.Set("LastSeen", NewId(), now);
            known.Set("OccurrenceCount", NewId(), count + 1);
            if (string.Equals(known.GetValue("Status") as string, ExceptionSignatureStatus.Fixed, StringComparison.Ordinal))
            {
                // it was declared fixed and it happened again: a regression is worth more attention than a new kind
                known.Set("Status", NewId(), ExceptionSignatureStatus.Regressed);
            }

            await UpdateAsync(known, cancellationToken).ConfigureAwait(false);
            return known.EntityId!.Value;
        }

        var id = Guid.NewGuid().ToString();
        var entry = new ExceptionSignatureEntry(schema.Schema);
        entry.Set(DynamicEntity.EntityIdPropertyName, id, id);
        entry.Set("Hash", NewId(), signature.Hash);
        entry.Set("ExceptionType", NewId(), signature.ExceptionType);
        entry.Set("MessagePattern", NewId(), signature.MessagePattern);
        entry.Set("TopFrame", NewId(), signature.TopFrame);
        entry.Set("FirstSeen", NewId(), now);
        entry.Set("LastSeen", NewId(), now);
        entry.Set("OccurrenceCount", NewId(), 1);
        entry.Set("Status", NewId(), NewStatus);
        await CreateAsync(entry, cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<IReadOnlyList<ExceptionSignatureSummary>> ListSummariesAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(entry => new ExceptionSignatureSummary(
                entry.EntityId!.Value,
                entry.GetValue("ExceptionType") as string ?? string.Empty,
                entry.GetValue("MessagePattern") as string ?? string.Empty,
                entry.GetValue("TopFrame") as string ?? string.Empty,
                entry.GetValue("FirstSeen") as DateTimeOffset?,
                entry.GetValue("LastSeen") as DateTimeOffset?,
                entry.GetValue("OccurrenceCount") is int count ? count : 0,
                entry.GetValue("Status") as string ?? NewStatus))
            .OrderByDescending(summary => summary.LastSeen)
            .ToList();

    public async Task<bool> SetStatusAsync(string signatureId, string status, CancellationToken cancellationToken = default)
    {
        if (!ExceptionSignatureStatus.Settable.Contains(status, StringComparer.Ordinal))
        {
            throw new Adventures.Common.Exceptions.InputValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status must be one of: {string.Join(", ", ExceptionSignatureStatus.Settable)}."],
            });
        }

        var entry = await GetAsync(signatureId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        entry.Set("Status", NewId(), status);
        await UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string NewId() => Guid.NewGuid().ToString();
}
