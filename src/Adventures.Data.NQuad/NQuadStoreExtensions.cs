namespace Adventures.Data.NQuad;

public static class NQuadStoreExtensions
{
    /// <summary>Parses a ".nq" file (see Sql/seed/seed.nq) and inserts every quad it contains into any store. Returns the number seeded.</summary>
    public static async Task<int> SeedFromFileAsync(this INQuadStore store, string nQuadFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var text = await File.ReadAllTextAsync(nQuadFilePath, cancellationToken);
        var quads = new NQuadFileParser().Parse(text);
        return await store.InsertManyAsync(quads, cancellationToken);
    }
}
