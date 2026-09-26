using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// xUnit runs different test classes in parallel by default. <see cref="NQuadStoreTests"/> and
/// <see cref="NQuadStoreKeyedServiceTests"/> both exercise the same real Postgres "n_quads" table (via
/// <see cref="NQuadStoreTestSupport.PurgeAndReseedAsync"/>), so they must share this collection to force
/// sequential execution and avoid one purging while the other counts/seeds.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NQuadStorePostgresCollection
{
    public const string Name = "NQuadStore Postgres";
}

/// <summary>
/// Shared helpers used by both <see cref="NQuadStoreTests"/> (hand-rolled <c>Func&lt;string, INQuadStore&gt;</c>
/// dispatcher) and <see cref="NQuadStoreKeyedServiceTests"/> (built-in keyed services), so both DI-registration
/// styles get exercised against the exact same store behaviors.
/// </summary>
internal static class NQuadStoreTestSupport
{
    internal static readonly string SeedFilePath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");

    /// <summary>
    /// Real-world reseed workflow for a Postgres-backed store resolved from DI: initializes the table (creating it
    /// if needed), purges whatever it currently holds, then seeds it from the official "seed.nq" file. Unlike
    /// <see cref="RunContractChecksAsync"/>, this deliberately leaves the seeded rows in place afterwards so the
    /// "n_quads" table ends up holding the latest seed data.
    /// </summary>
    internal static async Task PurgeAndReseedAsync(INQuadStore store)
    {
        if (store is INQuadStoreInitializer initializer)
        {
            await initializer.InitializeAsync();
        }

        await store.PurgeAsync();

        var seeded = await store.SeedFromFileAsync(SeedFilePath);

        Assert.True(seeded > 0);
        Assert.Equal(seeded, await store.CountAsync());
    }

    /// <summary>
    /// Drives an already-constructed <see cref="INQuadStore"/> (as resolved from DI) through every fact declared on
    /// <see cref="NQuadStoreContractTests"/>, purging between each one so every fact still sees the fresh/empty
    /// store it was written against. Only appropriate for the "memory" store: it is destructive (duplicate-id and
    /// cancellation checks, repeated purges) and is not appropriate to run against a real, shared database - see
    /// <see cref="PurgeAndReseedAsync"/> for the non-destructive Postgres workflow.
    /// </summary>
    internal static async Task RunContractChecksAsync(INQuadStore store)
    {
        var contract = new ProvidedStoreContractTests(store);
        await contract.InitializeAsync();
        try
        {
            foreach (var check in ProvidedStoreContractTests.Checks)
            {
                await store.PurgeAsync();
                await check(contract);
            }
        }
        finally
        {
            await contract.DisposeAsync();
        }
    }

    /// <summary>
    /// Adapts <see cref="NQuadStoreContractTests"/> so it runs its facts against a store instance supplied by the
    /// caller (e.g. resolved through DI) instead of one it constructs itself. Does not dispose the supplied store,
    /// since ownership stays with whoever built it.
    /// </summary>
    private sealed class ProvidedStoreContractTests(INQuadStore store) : NQuadStoreContractTests
    {
        public static readonly Func<ProvidedStoreContractTests, Task>[] Checks =
        [
            c => c.Insert_then_query_by_subject_returns_the_quad(),
            c => c.Insert_rejects_a_duplicate_id(),
            c => c.InsertMany_inserts_every_quad_and_returns_the_count(),
            c => c.InsertMany_with_an_empty_batch_returns_zero(),
            c => c.InsertMany_is_all_or_nothing_when_a_batch_contains_a_duplicate_id(),
            c => c.Query_with_no_terms_returns_every_quad(),
            c => c.Query_filters_by_each_term_and_by_combinations(),
            c => c.A_null_graph_round_trips_as_the_default_graph(),
            c => c.Purge_removes_every_quad(),
            c => c.An_already_cancelled_token_cancels_the_operation(),
            c => c.Initialize_can_be_called_repeatedly_without_losing_data(),
            c => c.Seeding_from_the_official_seed_file_loads_every_quad(),
        ];

        protected override Task<INQuadStore?> CreateStoreAsync() => Task.FromResult<INQuadStore?>(store);

        protected override Task DisposeStoreAsync() => Task.CompletedTask;
    }
}
