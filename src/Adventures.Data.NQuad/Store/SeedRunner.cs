using Adventures.Entities;

namespace Adventures.Data.NQuad;

/// <summary>The one generic seed runner (R2 of the host reuse review): initializes the store, applies the baseline file once, then each feature's named seeds in the order given.</summary>
public static class SeedRunner
{
    /// <summary>Returns whether the baseline was applied just now.</summary>
    public static async Task<bool> ApplyAsync(INQuadStore store, string baselineFile, IEnumerable<SeedEntry> seeds, string? seedDirectory = null)
    {
        if (store is INQuadStoreInitializer initializer)
        {
            await initializer.InitializeAsync();
        }

        var directory = seedDirectory ?? Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        var seeded = await store.SeedOnceAsync(Path.Combine(directory, baselineFile), "baseline");
        foreach (var seed in seeds)
        {
            await store.SeedOnceAsync(Path.Combine(directory, seed.File), seed.Name);
        }

        return seeded;
    }
}
