using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// The store-maintenance contract every <see cref="INQuadStore"/> must honour. Written against the interface so the same
/// facts run against <see cref="InMemoryNQuadStore"/> (always) and <see cref="NpgsqlNQuadStore"/> (when
/// "ConnectionStrings:Postgres" is configured; it uses a scratch table, never the dev "n_quads" table).
/// A backend that is unavailable returns a null store and each fact returns early, matching the existing opt-in Postgres tests.
/// By-id Get/Update/Delete and DynamicEntity CRUDL are a separate concern and deliberately not part of this contract.
/// </summary>
public abstract class NQuadStoreContractTests : IAsyncLifetime
{
    protected const string DcTitle = "http://purl.org/dc/terms/title";
    protected const string DcCreator = "http://purl.org/dc/terms/creator";
    private static readonly string SeedFilePath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");

    protected INQuadStore? Store { get; private set; }

    /// <summary>Creates a fresh, empty, initialized store, or null when the backend is not available.</summary>
    protected abstract Task<INQuadStore?> CreateStoreAsync();

    protected virtual Task DisposeStoreAsync() => Task.CompletedTask;

    public async Task InitializeAsync() => Store = await CreateStoreAsync();

    public Task DisposeAsync() => DisposeStoreAsync();

    private static NQuad Quad(string subject, string predicate, string @object, string? graph = "https://acme.example/blog") =>
        new(Guid.NewGuid(), subject, predicate, @object, graph);

    [Fact]
    public async Task Insert_then_query_by_subject_returns_the_quad()
    {
        if (Store is null) return;
        var quad = Quad("https://acme.example/post/1", DcTitle, "Welcome to Acme");

        var inserted = await Store.InsertAsync(quad);
        var found = await Store.QueryAsync(subject: quad.Subject);

        Assert.Equal(1, inserted);
        Assert.Equal([quad], found);
    }

    [Fact]
    public async Task Insert_rejects_a_duplicate_id()
    {
        if (Store is null) return;
        var quad = Quad("https://acme.example/post/1", DcTitle, "Welcome to Acme");
        await Store.InsertAsync(quad);

        // The exception TYPE is intentionally not pinned: exception handling is a separate concern (see the
        // exception-handling decision doc). The contract is that a duplicate id fails and adds nothing.
        await Assert.ThrowsAnyAsync<Exception>(() => Store.InsertAsync(quad with { Subject = "https://acme.example/post/2" }));

        Assert.Equal(1, await Store.CountAsync());
    }

    [Fact]
    public async Task InsertMany_inserts_every_quad_and_returns_the_count()
    {
        if (Store is null) return;
        var quads = new[]
        {
            Quad("https://acme.example/post/1", DcTitle, "One"),
            Quad("https://acme.example/post/2", DcTitle, "Two"),
            Quad("https://beta.example/post/1", DcTitle, "Three", "https://beta.example/blog"),
        };

        var inserted = await Store.InsertManyAsync(quads);

        Assert.Equal(3, inserted);
        Assert.Equal(3, await Store.CountAsync());
    }

    [Fact]
    public async Task InsertMany_with_an_empty_batch_returns_zero()
    {
        if (Store is null) return;

        Assert.Equal(0, await Store.InsertManyAsync([]));
        Assert.Equal(0, await Store.CountAsync());
    }

    [Fact]
    public async Task InsertMany_is_all_or_nothing_when_a_batch_contains_a_duplicate_id()
    {
        if (Store is null) return;
        var first = Quad("https://acme.example/post/1", DcTitle, "One");
        var clash = Quad("https://acme.example/post/2", DcTitle, "Two") with { Id = first.Id };

        await Assert.ThrowsAnyAsync<Exception>(() => Store.InsertManyAsync([first, clash]));

        Assert.Equal(0, await Store.CountAsync());
    }

    [Fact]
    public async Task Query_with_no_terms_returns_every_quad()
    {
        if (Store is null) return;
        var a = Quad("https://acme.example/post/1", DcTitle, "One");
        var b = Quad("https://beta.example/post/1", DcCreator, "Bea", "https://beta.example/blog");
        await Store.InsertManyAsync([a, b]);

        var all = await Store.QueryAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(a, all);
        Assert.Contains(b, all);
    }

    [Fact]
    public async Task Query_filters_by_each_term_and_by_combinations()
    {
        if (Store is null) return;
        var a = Quad("https://acme.example/post/1", DcTitle, "One");
        var b = Quad("https://acme.example/post/2", DcCreator, "Bea");
        var c = Quad("https://beta.example/post/1", DcTitle, "One", "https://beta.example/blog");
        await Store.InsertManyAsync([a, b, c]);

        Assert.Equal([b], await Store.QueryAsync(predicate: DcCreator));
        Assert.Equal(2, (await Store.QueryAsync(@object: "One")).Count);
        Assert.Equal([c], await Store.QueryAsync(graph: "https://beta.example/blog"));
        Assert.Equal([a], await Store.QueryAsync(predicate: DcTitle, graph: "https://acme.example/blog"));
        Assert.Empty(await Store.QueryAsync(subject: "https://nowhere.example/x"));
    }

    [Fact]
    public async Task A_null_graph_round_trips_as_the_default_graph()
    {
        if (Store is null) return;
        var quad = Quad("https://acme.example/post/1", DcTitle, "Default graph", graph: null);
        await Store.InsertAsync(quad);

        var found = await Store.QueryAsync(subject: quad.Subject);

        var single = Assert.Single(found);
        Assert.Null(single.Graph);
    }

    [Fact]
    public async Task Purge_removes_every_quad()
    {
        if (Store is null) return;
        await Store.InsertManyAsync([Quad("https://acme.example/post/1", DcTitle, "One"), Quad("https://acme.example/post/2", DcTitle, "Two")]);

        await Store.PurgeAsync();

        Assert.Equal(0, await Store.CountAsync());
        Assert.Empty(await Store.QueryAsync());
    }

    [Fact]
    public async Task An_already_cancelled_token_cancels_the_operation()
    {
        if (Store is null) return;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.CountAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.InsertAsync(Quad("https://acme.example/post/1", DcTitle, "x"), cts.Token));
    }

    [Fact]
    public async Task Initialize_can_be_called_repeatedly_without_losing_data()
    {
        if (Store is null) return;
        var initializer = Assert.IsAssignableFrom<INQuadStoreInitializer>(Store);
        await Store.InsertAsync(Quad("https://acme.example/post/1", DcTitle, "One"));

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        Assert.Equal(1, await Store.CountAsync());
    }

    [Fact]
    public async Task Seeding_from_the_official_seed_file_loads_every_quad()
    {
        if (Store is null) return;
        var expected = new NQuadFileParser().Parse(await File.ReadAllTextAsync(SeedFilePath)).Count;

        var seeded = await Store.SeedFromFileAsync(SeedFilePath);

        Assert.True(expected > 0);
        Assert.Equal(expected, seeded);
        Assert.Equal(expected, await Store.CountAsync());
    }
}
