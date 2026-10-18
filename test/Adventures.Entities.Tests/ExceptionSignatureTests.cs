using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>Exception signatures: what groups two exceptions, and the BLL's find-or-create with occurrence counting and CorrelationId on the log entry.</summary>
public sealed class ExceptionSignatureTests : IAsyncLifetime
{
    private InMemoryNQuadStore _store = null!;
    private ExceptionSignatureBll _signatures = null!;
    private ExceptionLogBll _log = null!;

    public async Task InitializeAsync()
    {
        _store = new InMemoryNQuadStore();
        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed.nq"), "baseline");
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed-002-exception-log.nq"), EntityConstants.ExceptionLog.SeedName);
        await _store.SeedOnceAsync(Path.Combine(seedDirectory, "seed-003-exception-signature.nq"), EntityConstants.ExceptionLog.SignatureSeedName);
        var quads = await _store.QueryAsync();

        var signatureSchema = new EntitySchemaOf<ExceptionSignatureEntry>(SchemaDal.Load(quads, EntityConstants.ExceptionSignatureEntity.SchemaIri));
        _signatures = new ExceptionSignatureBll(
            new NQuadEntityRepository<ExceptionSignatureEntry>(
                _store,
                signatureSchema.Schema,
                EntityConstants.ExceptionSignatureEntity.BaseIri,
                EntityConstants.ExceptionSignatureEntity.TypeIri,
                EntityConstants.ExceptionSignatureEntity.DefaultGraph,
                s => new ExceptionSignatureEntry(s)),
            signatureSchema);

        var logSchema = new EntitySchemaOf<ExceptionLogEntry>(SchemaDal.Load(quads, EntityConstants.ExceptionLog.SchemaIri));
        _log = new ExceptionLogBll(
            new NQuadEntityRepository<ExceptionLogEntry>(
                _store,
                logSchema.Schema,
                EntityConstants.ExceptionLog.BaseIri,
                EntityConstants.ExceptionLog.TypeIri,
                EntityConstants.ExceptionLog.DefaultGraph,
                s => new ExceptionLogEntry(s)),
            logSchema,
            signatures: _signatures);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Exception Throw(string message)
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
    public void Messages_that_differ_only_by_values_share_a_signature()
    {
        var a = ExceptionSignature.From(Throw("User 'alice' (id 41) not found: 3f2b8c1e-0d4a-4b7e-9a11-5c6d7e8f9a0b"));
        var b = ExceptionSignature.From(Throw("User 'bob' (id 977) not found: 11111111-2222-3333-4444-555555555555"));

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal("User '{value}' (id {n}) not found: {guid}", a.MessagePattern);
    }

    [Fact]
    public void Different_messages_types_or_origins_do_not_share_a_signature()
    {
        var baseline = ExceptionSignature.From(Throw("disk full"));

        Assert.NotEqual(baseline.Hash, ExceptionSignature.From(Throw("disk missing")).Hash);
        Assert.NotEqual(baseline.Hash, ExceptionSignature.From(new ArgumentException("disk full")).Hash);
        Assert.NotEqual(baseline.Hash, ExceptionSignature.From(Wrapped("disk full")).Hash);
    }

    [Fact]
    public void The_top_frame_is_the_first_application_frame_without_line_numbers()
    {
        var signature = ExceptionSignature.From(Throw("boom"));

        Assert.Equal("Adventures.Entities.Tests.ExceptionSignatureTests.Throw", signature.TopFrame);
    }

    [Fact]
    public async Task A_repeat_reuses_the_signature_and_counts_the_occurrence()
    {
        var first = await _signatures.RegisterOccurrenceAsync(Throw("db 'one' down"));
        var second = await _signatures.RegisterOccurrenceAsync(Throw("db 'two' down"));

        Assert.Equal(first, second);
        var entry = await _signatures.GetAsync(first);
        Assert.Equal(2, entry!.GetValue("OccurrenceCount"));
        Assert.Equal("New", entry.GetValue("Status"));
        Assert.Single(await _signatures.ListAsync());
        Assert.True((DateTimeOffset)entry.GetValue("LastSeen")! >= (DateTimeOffset)entry.GetValue("FirstSeen")!);
    }

    [Fact]
    public async Task Summaries_list_each_kind_with_its_count_most_recent_first()
    {
        await _signatures.RegisterOccurrenceAsync(Throw("older kind"));
        await Task.Delay(15);
        await _signatures.RegisterOccurrenceAsync(Throw("newer kind"));
        await Task.Delay(15);
        await _signatures.RegisterOccurrenceAsync(Throw("newer kind"));

        var summaries = await _signatures.ListSummariesAsync();

        Assert.Equal(["newer kind", "older kind"], summaries.Select(s => s.MessagePattern));
        Assert.Equal([2, 1], summaries.Select(s => s.OccurrenceCount));
        Assert.All(summaries, s => Assert.Equal("System.InvalidOperationException", s.ExceptionType));
    }

    [Fact]
    public async Task A_new_kind_of_failure_gets_its_own_signature()
    {
        var one = await _signatures.RegisterOccurrenceAsync(Throw("alpha"));
        var two = await _signatures.RegisterOccurrenceAsync(Throw("beta"));

        Assert.NotEqual(one, two);
        Assert.Equal(2, (await _signatures.ListAsync()).Count);
    }

    [Fact]
    public async Task The_log_entry_carries_the_signature_and_correlation_id()
    {
        var first = await _log.RecordAsync(Throw("boom 1"), "GET /a", "Admin", "corr-1");
        var second = await _log.RecordAsync(Throw("boom 2"), "GET /b", "Admin", "corr-2");

        var one = await _log.GetAsync(first!);
        var two = await _log.GetAsync(second!);
        Assert.Equal("corr-1", one!.GetValue("CorrelationId"));
        Assert.Equal("corr-2", two!.GetValue("CorrelationId"));
        Assert.Equal(one.GetValue("SignatureId"), two.GetValue("SignatureId"));
        var signature = await _signatures.GetAsync((string)one.GetValue("SignatureId")!);
        Assert.Equal(2, signature!.GetValue("OccurrenceCount"));
    }

    [Fact]
    public async Task Without_a_correlation_id_the_entry_is_still_recorded()
    {
        var id = await _log.RecordAsync(Throw("boom"), null, null);

        var entry = await _log.GetAsync(id!);
        Assert.Null(entry!.GetValue("CorrelationId"));
        Assert.NotNull(entry.GetValue("SignatureId"));
    }

    private static Exception Wrapped(string message)
    {
        try
        {
            try
            {
                throw new FormatException("root");
            }
            catch (Exception inner)
            {
                throw new InvalidOperationException(message, inner);
            }
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
