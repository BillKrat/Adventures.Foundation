using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="UserBll"/> against a real <see cref="NQuadEntityRepository{TEntity}"/> +
/// <see cref="InMemoryNQuadStore"/>, mirroring the pattern in
/// Adventures.Data.NQuad.Tests/SchemaEntityRepositoryTests.cs. Only <see cref="UserBll"/>'s own
/// behavior is exercised here (the UserName lookup and the delete-own-account guard) - ordinary
/// CRUDL passthrough is already covered by NQuadEntityRepositoryTests.
/// </summary>
public sealed class UserBllTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private InMemoryNQuadStore _store = null!;
    private UserBll _bll = null!;

    public async Task InitializeAsync()
    {
        _store = new InMemoryNQuadStore();
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");
        await _store.SeedFromFileAsync(seedPath);
        var allQuads = await _store.QueryAsync();
        var schema = SchemaDal.Load(allQuads, EntityConstants.Schema.UserIri);

        var repository = new NQuadEntityRepository<User>(
            _store,
            schema,
            EntityConstants.User.BaseIri,
            EntityConstants.User.TypeIri,
            Graph,
            s => new User(s));

        _bll = new UserBll(repository);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FindByUserNameAsync_ExistingUser_ReturnsUser()
    {
        var found = await _bll.FindByUserNameAsync("BillKrat");

        Assert.NotNull(found);
        Assert.Equal("BillKrat", found!.GetValue("UserName"));
    }

    [Fact]
    public async Task FindByUserNameAsync_CaseInsensitive_ReturnsUser()
    {
        var found = await _bll.FindByUserNameAsync("billkrat");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task FindByUserNameAsync_NoMatch_ReturnsNull()
    {
        var found = await _bll.FindByUserNameAsync("NoSuchUser");

        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteAsync_OwnAccount_Throws()
    {
        var bill = await _bll.FindByUserNameAsync("BillKrat");
        var billId = bill!.EntityId!.Value;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _bll.DeleteAsync(billId, billId));
    }

    [Fact]
    public async Task DeleteAsync_OtherAccount_Succeeds()
    {
        var bill = await _bll.FindByUserNameAsync("BillKrat");
        var billId = bill!.EntityId!.Value;

        var claudeId = Guid.NewGuid().ToString();
        var claude = new User(await LoadSchemaAsync())
            .Set(DynamicEntity.EntityIdPropertyName, claudeId, claudeId)
            .Set("UserName", Guid.NewGuid().ToString(), "Claude")
            .Set("First", Guid.NewGuid().ToString(), "Claude") as User
            ?? throw new InvalidOperationException();
        await _bll.CreateAsync(claude);

        var deleted = await _bll.DeleteAsync(claudeId, billId);

        Assert.True(deleted);
        Assert.Null(await _bll.GetAsync(claudeId));
    }

    private async Task<EntitySchema> LoadSchemaAsync()
    {
        var allQuads = await _store.QueryAsync();
        return SchemaDal.Load(allQuads, EntityConstants.Schema.UserIri);
    }
}
