// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventures.WebApi.Tests;

public sealed class FailureReportingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static (FailureReporter Reporter, UnhandledExceptionHandlerTests.RecordingExceptionLog Log) Build()
    {
        var log = new UnhandledExceptionHandlerTests.RecordingExceptionLog("e-1");
        var services = new ServiceCollection().AddSingleton<IExceptionLogBll>(log).BuildServiceProvider();
        return (new FailureReporter(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<FailureReporter>.Instance), log);
    }

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
    public void Journal_RoundTrips_AndSkipsAHalfWrittenLine()
    {
        FailureJournal.Write(_directory, "startup", Thrown("boom"));
        File.AppendAllText(Path.Combine(_directory, FailureJournal.FileName), "{\"At\":\"2026-10-2" + Environment.NewLine);

        var pending = FailureJournal.ReadPending(_directory);

        var entry = Assert.Single(pending);
        Assert.Equal("startup", entry.Source);
        Assert.Equal("System.InvalidOperationException", entry.Type);
        Assert.Equal("boom", entry.Message);
        Assert.Contains("FailureReportingTests", entry.Stack);
    }

    [Fact]
    public async Task Reporter_RecordsTheJournalInTheExceptionLog_ThenMarksItReported()
    {
        FailureJournal.Write(_directory, "startup", Thrown("could not open the database"));
        var (reporter, log) = Build();

        var count = await reporter.ReportJournalAsync(_directory, CancellationToken.None);

        Assert.Equal(1, count);
        var recorded = Assert.Single(log.Recorded);
        var failure = Assert.IsType<PersistedFailureException>(recorded.Exception);
        Assert.Contains("could not open the database", failure.Message);
        Assert.Contains("startup at", recorded.Request);
        Assert.Contains("FailureReportingTests", failure.StackTrace);
        Assert.Empty(FailureJournal.ReadPending(_directory));
        Assert.Single(Directory.GetFiles(_directory, "failures-reported-*.jsonl"));
        Assert.Equal(0, await reporter.ReportJournalAsync(_directory, CancellationToken.None));
    }

    [Fact]
    public async Task Reporter_KeepsTheJournal_WhenTheLogCannotRecord()
    {
        FailureJournal.Write(_directory, "startup", Thrown("x"));
        var services = new ServiceCollection().BuildServiceProvider();
        var reporter = new FailureReporter(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<FailureReporter>.Instance);

        Assert.Equal(0, await reporter.ReportJournalAsync(_directory, CancellationToken.None));
        Assert.Single(FailureJournal.ReadPending(_directory));
    }

    private sealed class Failing(IServiceScopeFactory scopes) : ObservedBackgroundService(scopes, NullLogger.Instance)
    {
        protected override Task ExecuteObservedAsync(CancellationToken stoppingToken) => throw new InvalidOperationException("worker broke");
    }

    [Fact]
    public async Task ObservedBackgroundService_RecordsItsFailure_AndStillFails()
    {
        var log = new UnhandledExceptionHandlerTests.RecordingExceptionLog("e-1");
        var services = new ServiceCollection().AddSingleton<IExceptionLogBll>(log).BuildServiceProvider();
        var worker = new Failing(services.GetRequiredService<IServiceScopeFactory>());

        await worker.StartAsync(CancellationToken.None);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await worker.ExecuteTask!);

        Assert.Equal("worker broke", failure.Message);
        var recorded = Assert.Single(log.Throttled);
        Assert.Equal("background service: Failing", recorded.Request);
    }
}
