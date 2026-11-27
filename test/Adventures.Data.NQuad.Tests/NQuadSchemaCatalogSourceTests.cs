using Adventures.Data.NQuad;
using Adventures.Entities;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

public class NQuadSchemaCatalogSourceTests
{
    private const string Thing = "https://schema.example/test/Thing";
    private const string NameField = Thing + "/Name";

    private static NQuad Quad(string subject, string predicate, string value, string graph = "urn:g") => new(Guid.NewGuid(), subject, predicate, value, graph);

    private static NQuad[] ThingSchema(string graph = "urn:g") =>
    [
        Quad(Thing, EntityConstants.Schema.TypePredicate, EntityConstants.Schema.TypeIri, graph),
        Quad(Thing, EntityConstants.Schema.FieldPredicate, NameField, graph),
        Quad(NameField, EntityConstants.Schema.FieldNamePredicate, "Name", graph),
        Quad(NameField, EntityConstants.Schema.FieldTypePredicate, "string", graph),
        Quad(NameField, EntityConstants.Schema.FieldRdfPredicate, "urn:p:name", graph),
        Quad(NameField, EntityConstants.Schema.FieldLabelPredicate, "Old label", graph),
    ];

    private static async Task<SchemaCatalog> LoadAsync(params NQuad[] quads)
    {
        var store = new InMemoryNQuadStore();
        await store.InsertManyAsync(quads);
        return await new NQuadSchemaCatalogSource(store).LoadAsync();
    }

    [Fact]
    public async Task The_seeded_schemas_are_listed_without_reading_the_whole_store()
    {
        var store = new RecordingStore(new InMemoryNQuadStore());
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq"));

        var catalog = await new NQuadSchemaCatalogSource(store).LoadAsync();

        var list = catalog.List();
        Assert.Empty(list.Unreadable);
        var user = Assert.Single(list.Schemas, s => s.Name == "User");
        Assert.Contains(catalog.Find(user.Id)!.Fields, f => f.Name == "UserName");
        Assert.Contains(list.Schemas, s => s.Name == "Schema" && s.Area == "schema");
        Assert.Contains(list.Schemas, s => s.Name == "Field" && s.Area == "schema");
        Assert.False(store.ReadEverything);
    }

    [Fact]
    public async Task A_schema_held_in_two_graphs_is_one_schema()
    {
        var catalog = await LoadAsync([.. ThingSchema("urn:one"), .. ThingSchema("urn:two")]);

        Assert.Equal(1, Assert.Single(catalog.List().Schemas).FieldCount);
    }

    [Fact]
    public async Task A_replaced_value_is_not_read()
    {
        var quads = ThingSchema();
        var old = quads[^1];

        var catalog = await LoadAsync(
        [
            .. quads,
            Quad(NameField, EntityConstants.Schema.FieldLabelPredicate, "New label"),
            Quad(old.Id.ToString(), EntityConstants.Lifecycle.SupersededAtPredicate, "2026-01-01T00:00:00Z"),
        ]);

        Assert.Equal("New label", Assert.Single(catalog.Find(Thing)!.Fields).Label);
    }

    [Fact]
    public async Task A_deleted_schema_is_gone_and_a_deleted_field_is_left_out()
    {
        const string second = Thing + "/Second";

        var deletedSchema = await LoadAsync([.. ThingSchema(), Quad(Thing, EntityConstants.Lifecycle.DeletedAtPredicate, "2026-01-01T00:00:00Z")]);
        var deletedField = await LoadAsync(
        [
            .. ThingSchema(),
            Quad(Thing, EntityConstants.Schema.FieldPredicate, second),
            Quad(second, EntityConstants.Schema.FieldNamePredicate, "Second"),
            Quad(second, EntityConstants.Schema.FieldTypePredicate, "string"),
            Quad(second, EntityConstants.Schema.FieldRdfPredicate, "urn:p:second"),
            Quad(second, EntityConstants.Lifecycle.DeletedAtPredicate, "2026-01-01T00:00:00Z"),
        ]);

        Assert.Empty(deletedSchema.List().Schemas);
        Assert.Empty(deletedSchema.List().Unreadable);
        Assert.Equal("Name", Assert.Single(deletedField.Find(Thing)!.Fields).Name);
    }

    [Fact]
    public async Task A_broken_schema_is_listed_as_unreadable_and_the_others_still_load()
    {
        const string broken = "https://schema.example/test/Broken";

        var catalog = await LoadAsync([.. ThingSchema(), Quad(broken, EntityConstants.Schema.TypePredicate, EntityConstants.Schema.TypeIri)]);

        Assert.Equal("Thing", Assert.Single(catalog.List().Schemas).Name);
        Assert.Equal([broken], catalog.List().Unreadable);
    }

    [Fact]
    public async Task An_empty_store_gives_an_empty_catalog() => Assert.Empty((await LoadAsync()).List().Schemas);

    private sealed class RecordingStore(INQuadStore inner) : INQuadStore
    {
        public bool ReadEverything { get; private set; }

        public Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default) => inner.InsertAsync(quad, cancellationToken);

        public Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default) => inner.InsertManyAsync(quads, cancellationToken);

        public Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default)
        {
            ReadEverything |= subject is null && predicate is null && @object is null && graph is null;
            return inner.QueryAsync(subject, predicate, @object, graph, cancellationToken);
        }

        public Task<IReadOnlyList<NQuad>> QueryBySubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default) => inner.QueryBySubjectsAsync(subjects, cancellationToken);

        public Task<long> CountAsync(CancellationToken cancellationToken = default) => inner.CountAsync(cancellationToken);

        public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => inner.PurgeAsync(cancellationToken);
    }
}
