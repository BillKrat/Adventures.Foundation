using Adventures.Entities;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Proves <see cref="SchemaEntity"/>/<see cref="SchemaFieldEntity"/> are servable through
/// <see cref="NQuadEntityRepository{TEntity}"/> exactly like <see cref="User"/> - no repository
/// changes were needed. Uses fresh ids for CRUDL, and separately proves <see cref="GetAsync"/> can
/// still read the legacy, hand-seeded "User" schema by id even though it predates this repository
/// (it was written with the <c>SchemaDal</c>-only <c>schema#type</c> marker, not a standard
/// rdf:type triple, so it deliberately does not show up in <see cref="ListAsync"/>).
/// </summary>
public sealed class SchemaEntityRepositoryTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private InMemoryNQuadStore _store = null!;
    private NQuadEntityRepository<SchemaEntity> _schemaRepository = null!;
    private NQuadEntityRepository<SchemaFieldEntity> _fieldRepository = null!;
    private int _nextId;

    public async Task InitializeAsync()
    {
        _store = new InMemoryNQuadStore();
        await _store.SeedFromFileAsync(NQuadStoreTestSupport.SeedFilePath);

        var clock = () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(_nextId++);

        _schemaRepository = new NQuadEntityRepository<SchemaEntity>(
            _store,
            SchemaEntity.MetaSchema,
            EntityConstants.Schema.EntityBaseIri,
            EntityConstants.Schema.EntityTypeIri,
            Graph,
            schema => new SchemaEntity(schema),
            clock: clock);

        _fieldRepository = new NQuadEntityRepository<SchemaFieldEntity>(
            _store,
            SchemaFieldEntity.MetaSchema,
            EntityConstants.Schema.FieldEntityBaseIri,
            EntityConstants.Schema.FieldEntityTypeIri,
            Graph,
            schema => new SchemaFieldEntity(schema),
            clock: clock);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SchemaFieldEntity NewField(string id, string name, string fieldType, string predicate) =>
        new SchemaFieldEntity(SchemaFieldEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, id, id)
            .Set("Name", Guid.NewGuid().ToString(), name)
            .Set("FieldType", Guid.NewGuid().ToString(), fieldType)
            .Set("Predicate", Guid.NewGuid().ToString(), predicate) as SchemaFieldEntity
        ?? throw new InvalidOperationException();

    [Fact]
    public async Task CreateAsync_SchemaFieldEntity_ThenGetAsync_RoundTrips()
    {
        var id = Guid.NewGuid().ToString();
        var field = NewField(id, "Nickname", "String", "urn:widget#nickname");

        await _fieldRepository.CreateAsync(field);
        var fetched = await _fieldRepository.GetAsync(id);

        Assert.NotNull(fetched);
        Assert.Equal("Nickname", fetched!.GetValue("Name"));
        Assert.Equal("urn:widget#nickname", fetched.GetValue("Predicate"));
    }

    [Fact]
    public async Task CreateAsync_SchemaEntityReferencingRealFieldEntities_RoundTrips()
    {
        var idFieldId = Guid.NewGuid().ToString();
        var nameFieldId = Guid.NewGuid().ToString();
        await _fieldRepository.CreateAsync(NewField(idFieldId, "Id", "Guid", "urn:widget#id"));
        await _fieldRepository.CreateAsync(NewField(nameFieldId, "Name", "String", "urn:widget#name"));

        var schemaId = "Widget-" + Guid.NewGuid();
        var schema = new SchemaEntity(SchemaEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, schemaId, schemaId)
            .Set("Field", Guid.NewGuid().ToString(), EntityConstants.Schema.FieldEntityBaseIri + idFieldId) as SchemaEntity
            ?? throw new InvalidOperationException();
        schema.Add("Field", Guid.NewGuid().ToString(), EntityConstants.Schema.FieldEntityBaseIri + nameFieldId);

        await _schemaRepository.CreateAsync(schema);
        var fetched = await _schemaRepository.GetAsync(schemaId);

        Assert.NotNull(fetched);
        Assert.Equal(2, fetched!.GetValues("Field").Count);
        Assert.Contains(EntityConstants.Schema.FieldEntityBaseIri + idFieldId, fetched.GetValues("Field"));
    }

    [Fact]
    public async Task UpdateAsync_SchemaFieldEntity_SupersedesOldQuad()
    {
        var id = Guid.NewGuid().ToString();
        var field = NewField(id, "Nickname", "String", "urn:widget#nickname");
        await _fieldRepository.CreateAsync(field);

        field.Set("Name", Guid.NewGuid().ToString(), "PreferredName");
        await _fieldRepository.UpdateAsync(field);

        var supersededMarkers = await _store.QueryAsync(predicate: EntityConstants.Lifecycle.SupersededAtPredicate);
        Assert.Single(supersededMarkers);

        var fetched = await _fieldRepository.GetAsync(id);
        Assert.Equal("PreferredName", fetched!.GetValue("Name"));
    }

    [Fact]
    public async Task DeleteAsync_SchemaEntity_TombstonesEntity_HiddenByDefaultVisibleWithIncludeDeleted()
    {
        var schemaId = "Widget-" + Guid.NewGuid();
        var schema = new SchemaEntity(SchemaEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, schemaId, schemaId)
            .Set("Field", Guid.NewGuid().ToString(), "urn:placeholder") as SchemaEntity
            ?? throw new InvalidOperationException();
        await _schemaRepository.CreateAsync(schema);

        Assert.True(await _schemaRepository.DeleteAsync(schemaId));
        Assert.Null(await _schemaRepository.GetAsync(schemaId));
        Assert.NotNull(await _schemaRepository.GetAsync(schemaId, includeDeleted: true));
    }

    [Fact]
    public async Task ListAsync_OnlyFindsRepositoryCreatedSchemas_NotTheLegacySeedAuthoredOnes()
    {
        var schemaId = "Widget-" + Guid.NewGuid();
        var schema = new SchemaEntity(SchemaEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, schemaId, schemaId)
            .Set("Field", Guid.NewGuid().ToString(), "urn:placeholder") as SchemaEntity
            ?? throw new InvalidOperationException();
        await _schemaRepository.CreateAsync(schema);

        var live = await _schemaRepository.ListAsync();

        Assert.Contains(live, s => s.GetValue("Id")!.ToString() == schemaId);
        // The seed-authored "User"/"AuditRecord" schema subjects predate this repository and were
        // written with SchemaDal's schema#type marker, not a standard rdf:type triple - by design
        // they are not discoverable via ListAsync, only by GetAsync(id) (see the fact below).
        Assert.DoesNotContain(live, s => s.GetValue("Id")!.ToString() == "User");
    }

    [Fact]
    public async Task GetAsync_LegacySeedAuthoredUserSchema_StillReadableById()
    {
        var fetched = await _schemaRepository.GetAsync("User");

        Assert.NotNull(fetched);
        var fieldIris = fetched!.GetValues("Field").Select(v => v.ToString()).ToArray();
        Assert.Contains(fieldIris, iri => iri!.EndsWith("/User/Id", StringComparison.Ordinal));
        Assert.Contains(fieldIris, iri => iri!.EndsWith("/User/Email", StringComparison.Ordinal));
    }
}
