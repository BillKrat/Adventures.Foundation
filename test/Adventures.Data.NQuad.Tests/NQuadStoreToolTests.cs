using Xunit;
using Xunit.Abstractions;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Doubles as the "purge/reseed/query" tool asked for alongside this library: each fact below
/// is independently runnable from Test Explorer against a real local Postgres (see
/// <see cref="PostgresConnectionFixture"/>) - create the table, purge it, reseed it from
/// nquad-end-to-end-poc's artifacts/seed.nq, and query it back. Every fact skips gracefully
/// (logs and returns, does not fail) when "ConnectionStrings:Postgres" is not configured, since
/// this is an opt-in integration tool, not something CI should require.
/// </summary>
[Collection(NQuadStorePostgresCollection.Name)]
public sealed class NQuadStoreToolTests(PostgresConnectionFixture connectionFixture, ITestOutputHelper output)
    : IClassFixture<PostgresConnectionFixture>
{
    private static readonly string SeedFilePath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");

    private bool TrySkip(out NpgsqlNQuadStore store)
    {
        if (connectionFixture.ConnectionString is null)
        {
            output.WriteLine("SKIPPED: ConnectionStrings:Postgres is not configured" +
                " (set via 'dotnet user-secrets set ConnectionStrings:Postgres ...' for this " +
                "test project to run against a real database).");
            store = null!;
            return true;
        }

        store = new NpgsqlNQuadStore(connectionFixture.ConnectionString);
        return false;
    }

    [Fact]
    public async Task Tool_CreateTable()
    {
        if (TrySkip(out var store))
        {
            return;
        }

        await store.CreateTableAsync();
        output.WriteLine("n_quads table created (or already existed).");
    }

    [Fact]
    public async Task Tool_PurgeTable()
    {
        if (TrySkip(out var store))
        {
            return;
        }

        await store.CreateTableAsync();
        var purged = await store.PurgeAsync();
        output.WriteLine($"Purged n_quads (rows affected by TRUNCATE reporting is provider-specific: {purged}).");
    }

    [Fact]
    public async Task Tool_SeedFromArtifact()
    {
        if (TrySkip(out var store))
        {
            return;
        }

        await store.CreateTableAsync();
        await store.PurgeAsync();
        var seeded = await store.SeedFromFileAsync(SeedFilePath);
        output.WriteLine($"Seeded {seeded} quads from {SeedFilePath}.");
        Assert.True(seeded > 0);
    }

    [Fact]
    public async Task Tool_QueryAll()
    {
        if (TrySkip(out var store))
        {
            return;
        }

        var quads = await store.QueryAsync();
        output.WriteLine($"n_quads currently contains {quads.Count} row(s).");
    }

    [Fact]
    public async Task PurgeReseedAndQuery_RoundTrips()
    {
        if (TrySkip(out var store))
        {
            return;
        }

        await store.CreateTableAsync();
        await store.PurgeAsync();

        var expectedCount = new NQuadFileParser().Parse(await File.ReadAllTextAsync(SeedFilePath)).Count;
        var seeded = await store.SeedFromFileAsync(SeedFilePath);
        Assert.Equal(expectedCount, seeded);

        var actualCount = await store.CountAsync();
        Assert.Equal(expectedCount, actualCount);

        var billQuads = await store.QueryAsync(subject: "https://global-webnet.com/id/user/b7963bd0-5ad3-4c83-b61c-fa622cc6a2df");
        Assert.NotEmpty(billQuads);
        Assert.Contains(billQuads, q => q.Predicate == "http://xmlns.com/foaf/0.1/givenName" && q.Object == "Bill");
    }
}

