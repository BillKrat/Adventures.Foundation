using Xunit;

namespace Adventures.Data.NQuad.Tests;

public sealed class NQuadStoreSeedOnceTests
{
    private static readonly string SeedFilePath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");

    [Fact]
    public async Task SeedOnceAsync_AppliesTheSeedAndRecordsAMarker()
    {
        var store = new InMemoryNQuadStore();

        var applied = await store.SeedOnceAsync(SeedFilePath, "baseline");

        Assert.True(applied);
        var markers = await store.QueryAsync(predicate: NQuadStoreExtensions.SeedAppliedPredicate);
        Assert.Equal("urn:adventures:seed:baseline", Assert.Single(markers).Subject);
    }

    [Fact]
    public async Task SeedOnceAsync_SecondCall_DoesNothing()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedOnceAsync(SeedFilePath, "baseline");
        var countAfterFirst = await store.CountAsync();

        var appliedAgain = await store.SeedOnceAsync(SeedFilePath, "baseline");

        Assert.False(appliedAgain);
        Assert.Equal(countAfterFirst, await store.CountAsync());
    }

    [Fact]
    public async Task SeedOnceAsync_DifferentName_IsAppliedSeparately()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedOnceAsync(SeedFilePath, "baseline");

        // A second, differently named seed (here the same content under a new name) is its own once-only unit.
        var applied = await store.SeedOnceAsync(SeedFilePath, "002-roles");

        Assert.True(applied);
    }
}
