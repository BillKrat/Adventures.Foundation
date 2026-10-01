using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="UserPresenter"/>'s two additions beyond the generic base: the UserName lookup and
/// the guarded delete. Ordinary CRUDL passthrough is already covered by
/// <see cref="EntityPresenterTests"/> and <c>NQuadEntityRepositoryTests</c>.
/// </summary>
public sealed class UserPresenterTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private IUserPresenter _presenter = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");
        await store.SeedFromFileAsync(seedPath);
        var allQuads = await store.QueryAsync();
        var schema = SchemaDal.Load(allQuads, EntityConstants.Schema.UserIri);

        var repository = new NQuadEntityRepository<User>(
            store, schema, EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, Graph, s => new User(s));
        var userBll = new UserBll(repository);

        _presenter = new UserPresenter(repository, userBll, schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetFormByUserNameAsync_ExistingUser_ReturnsForm()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");

        Assert.NotNull(form);
        Assert.Equal("Admin", form!.Entity.Values["UserName"]);
    }

    [Fact]
    public async Task GetFormByUserNameAsync_NoMatch_ReturnsNull()
    {
        Assert.Null(await _presenter.GetFormByUserNameAsync("NoSuchUser"));
    }

    [Fact]
    public async Task DeleteAsync_OwnAccount_Throws()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");
        var id = form!.Entity.EntityId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _presenter.DeleteAsync(id, id));
    }

    [Fact]
    public async Task DeleteAsync_OtherAccount_Succeeds()
    {
        var admin = await _presenter.GetFormByUserNameAsync("Admin");
        var adminId = admin!.Entity.EntityId;

        var created = await _presenter.CreateAsync(new EntityDataModel(
            string.Empty, new Dictionary<string, string?> { ["UserName"] = "Temp" }));

        var deleted = await _presenter.DeleteAsync(created.Entity.EntityId, adminId);

        Assert.True(deleted);
    }
}
