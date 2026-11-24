using Adventures.Entities;

namespace Adventures.Entities;

/// <summary>The seeds of the exception log and signature schemas.</summary>
public static class DiagnosticsSeeds
{
    /// <summary>In application order.</summary>
    public static IReadOnlyList<SeedEntry> All { get; } =
    [
        new("seed-002-exception-log.nq", ExceptionLogConstants.SeedName),
        new("seed-003-exception-signature.nq", ExceptionLogConstants.SignatureSeedName),
    ];
}

