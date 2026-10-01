using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="EntityPresenter{TEntity}"/> against a real <see cref="NQuadEntityRepository{TEntity}"/> +
/// <see cref="InMemoryNQuadStore"/>, using <see cref="SchemaEntity"/> as the entity under test -
/// proves the generic base needs zero entity-specific code (no SchemaPresenter subclass exists
/// here, same reasoning <c>NQuadEntityRepository{TEntity}</c> itself needs none).
/// </summary>
public sealed class EntityPresenterTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private IEntityPresenter<SchemaEntity> _presenter = null!;

    public Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var repository = new NQuadEntityRepository<SchemaEntity>(
            store,
            SchemaEntity.MetaSchema,
            EntityConstants.Schema.EntityBaseIri,
            EntityConstants.Schema.EntityTypeIri,
            Graph,
            schema => new SchemaEntity(schema));

        _presenter = new EntityPresenter<SchemaEntity>(repository, SchemaEntity.MetaSchema, schema => new SchemaEntity(schema));
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static EntityDataModel FieldData(string entityId, string value) =>
        new(entityId, new Dictionary<string, string?> { ["Field"] = value });

    [Fact]
    public async Task CreateAsync_ThenGetFormAsync_RoundTrips()
    {
        var created = await _presenter.CreateAsync(FieldData(string.Empty, "urn:widget#name"));

        var fetched = await _presenter.GetFormAsync(created.Entity.EntityId);

        Assert.NotNull(fetched);
        Assert.Equal("urn:widget#name", fetched!.Entity.Values["Field"]);
        Assert.Equal(EntityConstants.Schema.EntityTypeIri, fetched.Schema.SchemaIri);
    }

    [Fact]
    public async Task ListAsync_IncludesCreatedEntity()
    {
        var created = await _presenter.CreateAsync(FieldData(string.Empty, "urn:widget#x"));

        var all = await _presenter.ListAsync();

        Assert.Contains(all, e => e.EntityId == created.Entity.EntityId);
    }

    [Fact]
    public async Task UpdateAsync_ChangesValue()
    {
        var created = await _presenter.CreateAsync(FieldData(string.Empty, "urn:widget#x"));

        var updated = await _presenter.UpdateAsync(created.Entity.EntityId, FieldData(created.Entity.EntityId, "urn:widget#y"));

        Assert.NotNull(updated);
        Assert.Equal("urn:widget#y", updated!.Entity.Values["Field"]);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _presenter.UpdateAsync(Guid.NewGuid().ToString(), FieldData(string.Empty, "urn:widget#z")));
    }

    [Fact]
    public async Task DeleteAsync_TombstonesEntity()
    {
        var created = await _presenter.CreateAsync(FieldData(string.Empty, "urn:widget#x"));

        var deleted = await _presenter.DeleteAsync(created.Entity.EntityId);

        Assert.True(deleted);
        Assert.Null(await _presenter.GetFormAsync(created.Entity.EntityId));
    }

    [Fact]
    public async Task GetFormAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _presenter.GetFormAsync(Guid.NewGuid().ToString()));
    }
}
