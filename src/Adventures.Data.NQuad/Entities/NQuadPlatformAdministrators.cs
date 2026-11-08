using System.Globalization;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.NQuad;

/// <summary>
/// <see cref="IPlatformAdministrators"/> kept as plain statements in its own graph, outside every tenant's graph and outside the entity types (so no screen or generic
/// update can write it). Each change is one new statement about the person, <c>{utc timestamp};{true|false};{changed by}</c>, and the latest timestamp wins: nothing is
/// ever rewritten, so the history of the switch stays. The store has no delete, which is why a switch-off is a statement too.
/// </summary>
public sealed class NQuadPlatformAdministrators(INQuadStore store) : IPlatformAdministrators
{
    public const string Graph = "https://global-webnet.com/graph/platform";
    public const string Predicate = "https://global-webnet.com/ontology/platform#administrator";

    public async Task<bool> IsAsync(string userId, CancellationToken cancellationToken = default) =>
        (await AllAsync(cancellationToken).ConfigureAwait(false)).Contains(userId);

    public async Task<IReadOnlySet<string>> AllAsync(CancellationToken cancellationToken = default)
    {
        var statements = await store.QueryAsync(predicate: Predicate, graph: Graph, cancellationToken: cancellationToken).ConfigureAwait(false);
        return statements
            .Select(quad => (Subject: quad.Subject, Parsed: Parse(quad.Object)))
            .Where(item => item.Parsed is not null && item.Subject.StartsWith(EntityConstants.User.BaseIri, StringComparison.Ordinal))
            .GroupBy(item => item.Subject[EntityConstants.User.BaseIri.Length..], StringComparer.Ordinal)
            .Where(group => group.OrderByDescending(item => item.Parsed!.Value.At).First().Parsed!.Value.On)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task SetAsync(string userId, bool on, string changedBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var text = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow.UtcTicks};{(on ? "true" : "false")};{changedBy}");
        await store.InsertAsync(new NQuad(Guid.NewGuid(), EntityConstants.User.BaseIri + userId, Predicate, text, Graph), cancellationToken).ConfigureAwait(false);
    }

    private static (long At, bool On)? Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split(';', 3);
        return parts.Length == 3 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var at) && bool.TryParse(parts[1], out var on) ? (at, on) : null;
    }
}

public static class NQuadPlatformAdministratorsServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IPlatformAdministrators"/> over the N-Quad store and the <see cref="PlatformAdministratorBll"/> rules (per request).</summary>
    public static IServiceCollection AddNQuadPlatformAdministrators(this IServiceCollection services)
    {
        services.AddScoped<IPlatformAdministrators, NQuadPlatformAdministrators>();
        services.AddScoped<PlatformAdministratorBll>();
        return services;
    }
}
