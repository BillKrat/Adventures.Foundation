using Adventures.Entities;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

public class NQuadEntityRepositoryTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private InMemoryNQuadStore _store = null!;
    private EntitySchema _schema = null!;
    private NQuadEntityRepository<User> _repository = null!;
    private int _nextId;

    public async Task InitializeAsync()
    {
        _store = new InMemoryNQuadStore();
        await _store.SeedFromFileAsync(NQuadStoreTestSupport.SeedFilePath);
        var allQuads = await _store.QueryAsync();
        _schema = NQuadUserAdapter.LoadUserSchema(allQuads);
        _repository = new NQuadEntityRepository<User>(
            _store,
            _schema,
            EntityConstants.User.BaseIri,
            EntityConstants.User.TypeIri,
            Graph,
            schema => new User(schema),
            clock: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(_nextId++));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(EntitySchema schema, string id, string first, string last, string email) =>
        new User(schema)
            .Set(DynamicEntity.EntityIdPropertyName, id, id)
            .Set("First", Guid.NewGuid().ToString(), first)
            .Set("Last", Guid.NewGuid().ToString(), last)
            .Set("Email", Guid.NewGuid().ToString(), email) as User
        ?? throw new InvalidOperationException();

    [Fact]
    public async Task CreateAsync_ThenGetAsync_RoundTrips()
    {
        var id = Guid.NewGuid().ToString();
        var user = NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com");

        await _repository.CreateAsync(user);
        var fetched = await _repository.GetAsync(id);

        Assert.NotNull(fetched);
        Assert.Equal("Ada", fetched!.GetValue("First"));
        Assert.Equal("Lovelace", fetched.GetValue("Last"));
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_Throws()
    {
        var id = Guid.NewGuid().ToString();
        await _repository.CreateAsync(NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateAsync(NewUser(_schema, id, "Ada2", "Lovelace2", "mailto:ada2@example.com")));
    }

    [Fact]
    public async Task CreateAsync_WritesOneAuditRowPerField()
    {
        var id = Guid.NewGuid().ToString();
        await _repository.CreateAsync(NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com"));

        var auditQuads = await _store.QueryAsync(predicate: EntityConstants.AuditRecord.ActionPredicate, @object: "Create");
        Assert.Equal(3, auditQuads.Count); // First, Last, Email
    }

    [Fact]
    public async Task UpdateAsync_SupersedesOldQuad_NotPhysicallyRemoved()
    {
        var id = Guid.NewGuid().ToString();
        var user = NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com");
        await _repository.CreateAsync(user);

        var originalLastQuadId = user.GetFieldValue("Last")!.Id.Value;

        user.Set("Last", Guid.NewGuid().ToString(), "King");
        await _repository.UpdateAsync(user);

        // The old quad is still physically present in the store...
        var allUserQuads = await _store.QueryAsync(subject: EntityConstants.User.BaseIri + id, predicate: _schema.Fields["Last"].Predicate);
        Assert.Equal(2, allUserQuads.Count); // old "Lovelace" quad + new "King" quad, both still present

        var supersededMarkers = await _store.QueryAsync(predicate: EntityConstants.Lifecycle.SupersededAtPredicate);
        Assert.Single(supersededMarkers);
        Assert.Equal(originalLastQuadId, supersededMarkers[0].Subject);

        // ...but a fresh read only ever sees the current value.
        var fetched = await _repository.GetAsync(id);
        Assert.Equal("King", fetched!.GetValue("Last"));
    }

    [Fact]
    public async Task UpdateAsync_MultiValuedField_SupersedesEveryOriginalValue()
    {
        var id = Guid.NewGuid().ToString();
        var user = NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com");
        user.Add("Email", Guid.NewGuid().ToString(), "mailto:ada2@example.com");
        await _repository.CreateAsync(user);

        Assert.Equal(2, (await _repository.GetAsync(id))!.GetValues("Email").Count);

        user.Set("Email", Guid.NewGuid().ToString(), "mailto:ada3@example.com");
        await _repository.UpdateAsync(user);

        var supersededMarkers = await _store.QueryAsync(predicate: EntityConstants.Lifecycle.SupersededAtPredicate);
        Assert.Equal(2, supersededMarkers.Count); // both original Email quads superseded, not just the first

        var fetched = await _repository.GetAsync(id);
        Assert.Equal(["ada3@example.com"], fetched!.GetValues("Email")); // valuePrefix ("mailto:") is stripped on read
    }

    [Fact]
    public async Task UpdateAsync_OnDeletedEntity_Throws()
    {
        var id = Guid.NewGuid().ToString();
        var user = NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com");
        await _repository.CreateAsync(user);
        await _repository.DeleteAsync(id);

        user.Set("Last", Guid.NewGuid().ToString(), "King");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repository.UpdateAsync(user));
    }

    [Fact]
    public async Task DeleteAsync_TombstonesEntity_HiddenByDefaultVisibleWithIncludeDeleted()
    {
        var id = Guid.NewGuid().ToString();
        await _repository.CreateAsync(NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com"));

        var deleted = await _repository.DeleteAsync(id);
        Assert.True(deleted);

        Assert.Null(await _repository.GetAsync(id));

        var stillReadable = await _repository.GetAsync(id, includeDeleted: true);
        Assert.NotNull(stillReadable);
        Assert.Equal("Ada", stillReadable!.GetValue("First")); // field quads are untouched by delete

        var fieldQuads = await _store.QueryAsync(subject: EntityConstants.User.BaseIri + id, predicate: _schema.Fields["First"].Predicate);
        Assert.Single(fieldQuads); // nothing physically removed
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_ReturnsFalse()
    {
        var id = Guid.NewGuid().ToString();
        await _repository.CreateAsync(NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com"));
        await _repository.DeleteAsync(id);

        Assert.False(await _repository.DeleteAsync(id));
    }

    [Fact]
    public async Task DeleteAsync_WritesOneAuditRowPerField()
    {
        var id = Guid.NewGuid().ToString();
        await _repository.CreateAsync(NewUser(_schema, id, "Ada", "Lovelace", "mailto:ada@example.com"));
        await _repository.DeleteAsync(id);

        var deleteAudits = await _store.QueryAsync(predicate: EntityConstants.AuditRecord.ActionPredicate, @object: "Delete");
        Assert.Equal(3, deleteAudits.Count); // First, Last, Email
    }

    [Fact]
    public async Task ListAsync_ExcludesDeletedByDefault_IncludesWithFlag()
    {
        var keptId = Guid.NewGuid().ToString();
        var deletedId = Guid.NewGuid().ToString();
        await _repository.CreateAsync(NewUser(_schema, keptId, "Ada", "Lovelace", "mailto:ada@example.com"));
        await _repository.CreateAsync(NewUser(_schema, deletedId, "Grace", "Hopper", "mailto:grace@example.com"));
        await _repository.DeleteAsync(deletedId);

        // The seed data already contains one live User (Bill), so assert presence/absence by id
        // rather than an exact count - only relative counts and specific ids are this test's concern.
        var live = await _repository.ListAsync();
        Assert.Contains(live, u => u.GetValue("Id")!.ToString() == keptId);
        Assert.DoesNotContain(live, u => u.GetValue("Id")!.ToString() == deletedId);

        var all = await _repository.ListAsync(includeDeleted: true);
        Assert.Equal(live.Count + 1, all.Count);
        Assert.Contains(all, u => u.GetValue("Id")!.ToString() == deletedId);
    }
}
