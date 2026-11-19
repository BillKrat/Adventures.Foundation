using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>The exception log drill-down: from a kind of failure to the occurrences that belong to it.</summary>
public sealed class ExceptionDrillDownTests : IAsyncLifetime
{
    private ExceptionLogBll _log = null!;
    private ExceptionLogPresenter _presenter = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedOnceAsync(Path.Combine(seeds, "seed.nq"), "baseline");
        await store.SeedOnceAsync(Path.Combine(seeds, "seed-002-exception-log.nq"), ExceptionLogConstants.SeedName);
        await store.SeedOnceAsync(Path.Combine(seeds, "seed-003-exception-signature.nq"), ExceptionLogConstants.SignatureSeedName);
        var quads = await store.QueryAsync();

        var signatureSchema = new EntitySchemaOf<ExceptionSignatureEntry>(SchemaDal.Load(quads, ExceptionSignatureConstants.SchemaIri));
        var signatures = new ExceptionSignatureBll(
            new EntityDal<ExceptionSignatureEntry>(new NQuadEntityRepository<ExceptionSignatureEntry>(
                store, signatureSchema.Schema, ExceptionSignatureConstants.BaseIri, ExceptionSignatureConstants.TypeIri,
                ExceptionSignatureConstants.DefaultGraph, s => new ExceptionSignatureEntry(s))),
            signatureSchema);
        var logSchema = new EntitySchemaOf<ExceptionLogEntry>(SchemaDal.Load(quads, ExceptionLogConstants.SchemaIri));
        _log = new ExceptionLogBll(
            new EntityDal<ExceptionLogEntry>(new NQuadEntityRepository<ExceptionLogEntry>(
                store, logSchema.Schema, ExceptionLogConstants.BaseIri, ExceptionLogConstants.TypeIri,
                ExceptionLogConstants.DefaultGraph, s => new ExceptionLogEntry(s))),
            logSchema,
            signatures: signatures);
        _presenter = new ExceptionLogPresenter(_log, logSchema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Exception Throw(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    private async Task<(string KindA, string KindB)> RecordAsync()
    {
        var a1 = await _log.RecordAsync(Throw(new InvalidOperationException("boom 1")), "GET /a", "Admin", "corr-a1");
        await _log.RecordAsync(Throw(new InvalidOperationException("boom 2")), "GET /b", "Admin", "corr-a2");
        var b1 = await _log.RecordAsync(Throw(new TimeoutException("slow")), "GET /c", "Admin", "corr-b1");
        return (
            (string)(await _log.GetAsync(a1!))!.GetValue("SignatureId")!,
            (string)(await _log.GetAsync(b1!))!.GetValue("SignatureId")!);
    }

    [Fact]
    public async Task ADrillDownListsOnlyTheOccurrencesOfThatKind_NewestFirst()
    {
        var (kindA, kindB) = await RecordAsync();

        var a = await _presenter.GetSignatureGridAsync(kindA, includePurged: false);
        var b = await _presenter.GetSignatureGridAsync(kindB, includePurged: false);

        Assert.Equal(2, a.Rows.Count);
        Assert.All(a.Rows, row => Assert.Equal(kindA, row.Values["SignatureId"]));
        Assert.Equal(["corr-a2", "corr-a1"], a.Rows.Select(row => row.Values["CorrelationId"]));
        Assert.Single(b.Rows);
        Assert.Equal("corr-b1", b.Rows[0].Values["CorrelationId"]);
    }

    [Fact]
    public async Task AnUnknownKindHasNoOccurrences()
    {
        await RecordAsync();

        Assert.Empty((await _presenter.GetSignatureGridAsync("no-such-kind", includePurged: false)).Rows);
        Assert.Empty((await _presenter.GetSignatureGridAsync(string.Empty, includePurged: false)).Rows);
    }

    [Fact]
    public async Task PurgedOccurrencesAreInTheHistoryOnly_MarkedPurged()
    {
        var (kindA, _) = await RecordAsync();
        await _presenter.PurgeAsync();

        Assert.Empty((await _presenter.GetSignatureGridAsync(kindA, includePurged: false)).Rows);
        var history = await _presenter.GetSignatureGridAsync(kindA, includePurged: true);
        Assert.Equal(2, history.Rows.Count);
        Assert.All(history.Rows, row => Assert.Equal("Purged", row.Values["State"]));
    }
}
