using Adventures.Data.NQuad;
using Adventures.MEF;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>Plug-ins hear about every recorded exception: one event after the entry is stored, scrubbed of secrets, and never able to break the recording.</summary>
public sealed class ExceptionRecordedEventTests : IAsyncLifetime
{
    private InMemoryNQuadStore _store = null!;
    private EntitySchemaOf<ExceptionLogEntry> _schema = null!;

    private sealed class FakeEvents(bool throws = false) : IPluginEvents
    {
        public List<IPluginEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent pluginEvent, CancellationToken cancellationToken = default)
            where TEvent : IPluginEvent
        {
            if (throws)
            {
                throw new InvalidOperationException("the bus is broken");
            }

            Published.Add(pluginEvent);
            return Task.CompletedTask;
        }

        public Task<InterceptResult> InterceptAsync<TEvent>(TEvent pluginEvent, CancellationToken cancellationToken = default)
            where TEvent : IPluginEvent => Task.FromResult(InterceptResult.Allow);
    }

    public async Task InitializeAsync()
    {
        _store = new InMemoryNQuadStore();
        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed.nq"), "baseline");
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed-002-exception-log.nq"), ExceptionLogConstants.SeedName);
        _schema = new EntitySchemaOf<ExceptionLogEntry>(SchemaDal.Load(await _store.QueryAsync(), ExceptionLogConstants.SchemaIri));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ExceptionLogBll Bll(IPluginEvents? events, INQuadStore? store = null) => new(
        new EntityDal<ExceptionLogEntry>(new NQuadEntityRepository<ExceptionLogEntry>(
            store ?? _store, _schema.Schema, ExceptionLogConstants.BaseIri, ExceptionLogConstants.TypeIri, ExceptionLogConstants.DefaultGraph, s => new ExceptionLogEntry(s))),
        _schema,
        events: events);

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task ARecordedException_RaisesOneEvent_WithTheEntryIdAndTheCorrelationId()
    {
        var events = new FakeEvents();

        var id = await Bll(events).RecordAsync(Thrown("boom"), "GET /x", "bill", "corr-1");

        var raised = Assert.IsType<ExceptionRecordedEvent>(Assert.Single(events.Published));
        Assert.Equal(id, raised.EntryId);
        Assert.Equal("System.InvalidOperationException", raised.ExceptionType);
        Assert.Equal("boom", raised.Message);
        Assert.Equal("corr-1", raised.CorrelationId);
    }

    [Fact]
    public async Task TheEventMessage_IsScrubbedOfSecrets_AndShortened()
    {
        var events = new FakeEvents();

        await Bll(events).RecordAsync(Thrown("login failed Password=hunter2; Server=db " + new string('x', 2000)), null, null);

        var message = Assert.IsType<ExceptionRecordedEvent>(Assert.Single(events.Published)).Message;
        Assert.DoesNotContain("hunter2", message);
        Assert.True(message.Length <= 600);
    }

    [Fact]
    public async Task WhenTheEntryCannotBeStored_NoEventIsRaised()
    {
        var events = new FakeEvents();

        var id = await Bll(events, new ThrowingStore()).RecordAsync(Thrown("boom"), null, null);

        Assert.Null(id);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task ABrokenBus_NeverBreaksTheRecording()
    {
        var id = await Bll(new FakeEvents(throws: true)).RecordAsync(Thrown("boom"), null, null);

        Assert.NotNull(id);
    }

    [Fact]
    public async Task WithNoBus_RecordingIsAsBefore()
    {
        Assert.NotNull(await Bll(null).RecordAsync(Thrown("boom"), null, null));
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
