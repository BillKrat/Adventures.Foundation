// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>The exception log: record, count, grid (newest first), purge as a soft delete - against a real repository and in-memory store.</summary>
public sealed class ExceptionLogTests : IAsyncLifetime
{
    private InMemoryNQuadStore _store = null!;
    private ExceptionLogBll _bll = null!;
    private ExceptionLogPresenter _presenter = null!;

    public async Task InitializeAsync()
    {
        _store = new InMemoryNQuadStore();
        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed.nq"), "baseline");
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed-002-exception-log.nq"), EntityConstants.ExceptionLog.SeedName);

        var schema = new EntitySchemaOf<ExceptionLogEntry>(
            SchemaDal.Load(await _store.QueryAsync(), EntityConstants.ExceptionLog.SchemaIri));
        var repository = new NQuadEntityRepository<ExceptionLogEntry>(
            _store,
            schema.Schema,
            EntityConstants.ExceptionLog.BaseIri,
            EntityConstants.ExceptionLog.TypeIri,
            EntityConstants.ExceptionLog.DefaultGraph,
            s => new ExceptionLogEntry(s));

        _bll = new ExceptionLogBll(new EntityDal<ExceptionLogEntry>(repository), schema);
        _presenter = new ExceptionLogPresenter(_bll, schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message, new FormatException("inner detail"));
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task RecordAsync_StoresTypeMessageStackTraceRequestAndUser()
    {
        var id = await _bll.RecordAsync(Thrown("boom"), "PUT /api/profile/me", "Admin");

        var entry = await _bll.GetAsync(id!);
        Assert.Equal("System.InvalidOperationException", entry!.GetValue("ExceptionType"));
        Assert.Equal("boom", entry.GetValue("Message"));
        Assert.Contains("inner detail", (string)entry.GetValue("StackTrace")!);
        Assert.Contains("ExceptionLogTests.Thrown", (string)entry.GetValue("StackTrace")!);
        Assert.Equal("PUT /api/profile/me", entry.GetValue("Request"));
        Assert.Equal("Admin", entry.GetValue("UserName"));
        Assert.IsType<DateTimeOffset>(entry.GetValue("OccurredAt"));
    }

    [Fact]
    public async Task RecordAsync_WithNoRequestOrUser_StillRecords()
    {
        var id = await _bll.RecordAsync(Thrown("boom"), request: null, userName: null);

        Assert.NotNull(await _bll.GetAsync(id!));
    }

    [Fact]
    public async Task RecordAsync_TruncatesAVeryLongMessage()
    {
        var id = await _bll.RecordAsync(Thrown(new string('x', 300_000)), null, null);

        var message = (string)(await _bll.GetAsync(id!))!.GetValue("Message")!;
        Assert.True(message.Length < 251_000);
        Assert.EndsWith("[truncated 50000 chars]", message);
    }

    [Fact]
    public async Task RecordAsync_WhenTheStoreFails_ReturnsNullInsteadOfThrowing()
    {
        var failing = new NQuadEntityRepository<ExceptionLogEntry>(
            new ThrowingStore(),
            SchemaDal.Load(await _store.QueryAsync(), EntityConstants.ExceptionLog.SchemaIri),
            EntityConstants.ExceptionLog.BaseIri,
            EntityConstants.ExceptionLog.TypeIri,
            EntityConstants.ExceptionLog.DefaultGraph,
            s => new ExceptionLogEntry(s));
        var bll = new ExceptionLogBll(new EntityDal<ExceptionLogEntry>(failing), new EntitySchemaOf<ExceptionLogEntry>(
            SchemaDal.Load(await _store.QueryAsync(), EntityConstants.ExceptionLog.SchemaIri)));

        var id = await bll.RecordAsync(Thrown("boom"), null, null);

        Assert.Null(id);
    }

    [Fact]
    public async Task CountActiveAsync_CountsOnlyUnpurgedEntries()
    {
        await _bll.RecordAsync(Thrown("one"), null, null);
        await _bll.RecordAsync(Thrown("two"), null, null);

        Assert.Equal(2, await _bll.CountActiveAsync());
    }

    [Fact]
    public async Task PurgeAsync_TombstonesEveryEntryAndKeepsTheHistory()
    {
        var first = await _bll.RecordAsync(Thrown("one"), null, null);
        await _bll.RecordAsync(Thrown("two"), null, null);

        var purged = await _bll.PurgeAsync();

        Assert.Equal(2, purged);
        Assert.Equal(0, await _bll.CountActiveAsync());
        Assert.Null(await _bll.GetAsync(first!));
        Assert.Empty((await _presenter.GetGridAsync()).Rows);
        // soft delete: the entries are still in the store, each marked deleted
        Assert.Equal(2, (await _store.QueryAsync(predicate: EntityConstants.Lifecycle.DeletedAtPredicate)).Count);
    }

    [Fact]
    public async Task GetGridAsync_ListsNewestFirstWithTheSchemaColumns()
    {
        var older = await _bll.RecordAsync(Thrown("older"), null, null);
        await Task.Delay(15);
        var newer = await _bll.RecordAsync(Thrown("newer"), null, null);

        var grid = await _presenter.GetGridAsync();

        Assert.Equal([newer, older], grid.Rows.Select(row => row.EntityId));
        Assert.Contains(grid.Schema.Fields, field => field.Name == "StackTrace");
        Assert.Contains(grid.Schema.Fields, field => field.Name == "OccurredAt" && field.Type == "DateTimeOffset");
    }

    [Fact]
    public async Task GetHistoryGridAsync_ShowsPurgedEntriesToo_MarkedByState()
    {
        var purgedId = await _bll.RecordAsync(Thrown("old"), null, null);
        await _presenter.PurgeAsync();
        await Task.Delay(15);
        var activeId = await _bll.RecordAsync(Thrown("new"), null, null);

        var history = await _presenter.GetHistoryGridAsync();

        Assert.Equal([activeId, purgedId], history.Rows.Select(row => row.EntityId));
        Assert.Equal("Active", history.Rows[0].Values["State"]);
        Assert.Equal("Purged", history.Rows[1].Values["State"]);
        Assert.Contains(history.Schema.Fields, field => field.Name == "State");
        Assert.Equal(2, await _presenter.CountTotalAsync());
        Assert.Equal(1, await _presenter.CountActiveAsync());
        // the ordinary grid still hides purged entries
        Assert.Single((await _presenter.GetGridAsync()).Rows);
    }

    [Fact]
    public async Task GetGridAsync_AfterPurge_IsEmpty()
    {
        await _bll.RecordAsync(Thrown("one"), null, null);
        await _presenter.PurgeAsync();

        Assert.Empty((await _presenter.GetGridAsync()).Rows);
        Assert.Equal(0, await _presenter.CountActiveAsync());
    }

    private sealed class ThrowingStore : INQuadStore
    {
        public Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default) => throw new InvalidOperationException("store down");

        public Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default) => throw new InvalidOperationException("store down");

        public Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("store down");

        public Task<IReadOnlyList<NQuad>> QueryBySubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default) => throw new InvalidOperationException("store down");

        public Task<long> CountAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("store down");

        public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("store down");
    }
}
