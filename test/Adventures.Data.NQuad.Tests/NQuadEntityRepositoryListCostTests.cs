using Adventures.Entities;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Listing must cost a constant number of store round trips however many entities exist - against a remote database an
/// N+1 loop is the difference between milliseconds and seconds.
/// </summary>
public sealed class NQuadEntityRepositoryListCostTests
{
    private sealed class Widget(EntitySchema schema) : DynamicEntity(schema);

    private const string SchemaIri = "urn:test:Widget";

    private static readonly EntitySchema Schema = EntitySchema.Create(SchemaIri,
    [
        new EntitySchemaField(SchemaIri, "f-id", "Id", "string", "urn:p:id"),
        new EntitySchemaField(SchemaIri, "f-name", "Name", "string", "urn:p:name"),
    ]);

    private sealed class CountingStore(INQuadStore inner) : INQuadStore
    {
        public int Calls { get; private set; }

        public Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default) { Calls++; return inner.InsertAsync(quad, cancellationToken); }

        public Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default) { Calls++; return inner.InsertManyAsync(quads, cancellationToken); }

        public Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default) { Calls++; return inner.QueryAsync(subject, predicate, @object, graph, cancellationToken); }

        public Task<IReadOnlyList<NQuad>> QueryBySubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default) { Calls++; return inner.QueryBySubjectsAsync(subjects, cancellationToken); }

        public Task<long> CountAsync(CancellationToken cancellationToken = default) { Calls++; return inner.CountAsync(cancellationToken); }

        public Task<int> PurgeAsync(CancellationToken cancellationToken = default) { Calls++; return inner.PurgeAsync(cancellationToken); }
    }

    private static async Task<(NQuadEntityRepository<Widget> Repository, CountingStore Store)> SeedAsync(int count)
    {
        var store = new CountingStore(new InMemoryNQuadStore());
        var repository = new NQuadEntityRepository<Widget>(store, Schema, "urn:test:widget/", SchemaIri, "urn:test:graph", s => new Widget(s));
        for (var i = 0; i < count; i++)
        {
            var widget = new Widget(Schema);
            widget.Set("Id", $"w{i}", $"w{i}");
            widget.Set("Name", Guid.NewGuid().ToString(), $"Name {i}");
            await repository.CreateAsync(widget);
        }

        return (repository, store);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(60)]
    public async Task ListAsync_UsesAConstantNumberOfStoreCalls(int entityCount)
    {
        var (repository, store) = await SeedAsync(entityCount);
        var before = store.Calls;

        var all = await repository.ListAsync();

        Assert.Equal(entityCount, all.Count);
        Assert.Equal(3, store.Calls - before);
    }

    [Fact]
    public async Task ListAsync_HonoursSupersededValuesAndTombstones()
    {
        var (repository, _) = await SeedAsync(3);
        var w1 = (await repository.GetAsync("w1"))!;
        w1.Set("Name", Guid.NewGuid().ToString(), "Renamed");
        await repository.UpdateAsync(w1);
        await repository.DeleteAsync("w2");

        var all = await repository.ListAsync();

        Assert.Equal(["Name 0", "Renamed"], all.Select(w => (string)w.GetValue("Name")!).Order().ToArray());
        Assert.Equal(3, (await repository.ListAsync(includeDeleted: true)).Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(40)]
    public async Task DeleteManyAsync_UsesAConstantNumberOfStoreCalls(int entityCount)
    {
        var (repository, store) = await SeedAsync(entityCount);
        var ids = Enumerable.Range(0, entityCount).Select(i => $"w{i}").ToArray();
        var before = store.Calls;

        var deleted = await repository.DeleteManyAsync(ids);

        Assert.Equal(entityCount, deleted);
        Assert.Equal(3, store.Calls - before);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task DeleteManyAsync_SkipsMissingAndAlreadyDeleted_AndCountsOnlyWhatItDeleted()
    {
        var (repository, _) = await SeedAsync(3);
        await repository.DeleteAsync("w0");

        var deleted = await repository.DeleteManyAsync(["w0", "w1", "w1", "nope"]);

        Assert.Equal(1, deleted);
        Assert.Equal(["w2"], (await repository.ListAsync()).Select(w => (string)w.GetValue("Id")!));
    }

    [Fact]
    public async Task DeleteManyAsync_WritesTheSameAuditRowsAsDeletingOneByOne()
    {
        var (batch, batchStore) = await SeedAsync(2);
        var (single, singleStore) = await SeedAsync(2);

        await batch.DeleteManyAsync(["w0", "w1"]);
        await single.DeleteAsync("w0");
        await single.DeleteAsync("w1");

        var batchAudits = (await batchStore.QueryAsync(predicate: EntityConstants.AuditRecord.ActionPredicate, @object: "Delete")).Count;
        var singleAudits = (await singleStore.QueryAsync(predicate: EntityConstants.AuditRecord.ActionPredicate, @object: "Delete")).Count;
        Assert.Equal(singleAudits, batchAudits);
        Assert.True(batchAudits > 0);
    }

    [Fact]
    public async Task DeleteManyAsync_WithNoIds_DoesNothing()
    {
        var (repository, store) = await SeedAsync(2);
        var before = store.Calls;

        Assert.Equal(0, await repository.DeleteManyAsync([]));
        Assert.Equal(before, store.Calls);
    }

    [Fact]
    public async Task GetAsync_UsesTwoStoreCalls_NotAScanOfEveryMarker()
    {
        var (repository, store) = await SeedAsync(5);
        var before = store.Calls;

        await repository.GetAsync("w3");

        Assert.Equal(2, store.Calls - before);
    }
}
