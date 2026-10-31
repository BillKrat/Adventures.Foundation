// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Data.NQuad;

public static class NQuadStoreExtensions
{
    /// <summary>Subject of the quad that records a named seed as applied.</summary>
    private const string SeedMarkerSubjectPrefix = "urn:adventures:seed:";

    /// <summary>Predicate of the "this seed was applied" marker quad.</summary>
    public const string SeedAppliedPredicate = "urn:adventures:seed#appliedAt";

    private const string SystemGraph = "urn:adventures:system";

    /// <summary>Parses a ".nq" file (see Sql/seed/seed.nq) and inserts every quad it contains into any store. Returns the number seeded.</summary>
    public static async Task<int> SeedFromFileAsync(this INQuadStore store, string nQuadFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var text = await File.ReadAllTextAsync(nQuadFilePath, cancellationToken);
        var quads = new NQuadFileParser().Parse(text);
        return await store.InsertManyAsync(quads, cancellationToken);
    }

    /// <summary>
    /// Applies the seed file <paramref name="nQuadFilePath"/> once, recording that in the store itself under
    /// <paramref name="seedName"/> (the marker and the seed go in one batch, so a crash cannot leave one without the other).
    /// A later start finds the marker and does nothing. Returns true when the seed was applied, false when it already had been.
    /// Deliberately never re-applies or merges: once a seed quad has been edited (superseded), re-inserting the original
    /// would resurrect the old value. A seed change ships as a new, separately named seed file.
    /// </summary>
    public static async Task<bool> SeedOnceAsync(this INQuadStore store, string nQuadFilePath, string seedName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedName);

        var marker = SeedMarkerSubjectPrefix + seedName;
        if ((await store.QueryAsync(subject: marker, predicate: SeedAppliedPredicate, cancellationToken: cancellationToken)).Count > 0)
        {
            return false;
        }

        var text = await File.ReadAllTextAsync(nQuadFilePath, cancellationToken);
        var quads = new NQuadFileParser().Parse(text).ToList();
        quads.Add(new NQuad(Guid.NewGuid(), marker, SeedAppliedPredicate, DateTimeOffset.UtcNow.ToString("O"), SystemGraph));
        await store.InsertManyAsync(quads, cancellationToken);
        return true;
    }
}
