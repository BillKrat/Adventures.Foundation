using Adventures.Entities;

namespace Adventures.Blog.Legacy;

/// <summary>The seeds of the legacy mirror layer: one schema per ported BlogEngine.NET table, and its id fields.</summary>
public static class LegacySeeds
{
    /// <summary>In application order.</summary>
    public static IReadOnlyList<SeedEntry> All { get; } =
    [
        new("seed-009-legacy-schemas.nq", EntityConstants.Legacy.SchemaSeedName),
        new("seed-010-legacy-ids.nq", EntityConstants.Legacy.IdFieldsSeedName),
    ];
}

