using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Adventures.WebApi;

/// <summary>
/// Closes the "silent failure" gaps that never pass through a request: it reports earlier startup crashes and process-level
/// failures that were written to the <see cref="FailureJournal"/> (on start, once the store is reachable), writes a crash
/// that is killing the process to the journal, and records unobserved task exceptions (a fire-and-forget task that failed)
/// in the exception log, throttled per kind of failure.
/// </summary>
public sealed class FailureReporter(IServiceScopeFactory scopes, ILogger<FailureReporter> logger) : IHostedService
{
    public static readonly TimeSpan UnobservedWindow = TimeSpan.FromHours(1);
    private readonly string _directory = FailureJournal.DefaultDirectory;

    /// <summary>Reports the journal in <paramref name="directory"/> now and returns how many entries reached the exception log.</summary>
    public async Task<int> ReportJournalAsync(string directory, CancellationToken cancellationToken)
    {
        var pending = FailureJournal.ReadPending(directory);
        if (pending.Count == 0)
        {
            return 0;
        }

        using var scope = scopes.CreateScope();
        var log = scope.ServiceProvider.GetService<IExceptionLogBll>();
        if (log is null)
        {
            return 0;
        }

        foreach (var entry in pending)
        {
            await log.RecordAsync(
                new PersistedFailureException(entry.Type, entry.Message, entry.Stack),
                $"{entry.Source} at {entry.At:u}", null, null, cancellationToken);
        }

        FailureJournal.MarkReported(directory);
        return pending.Count;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var reported = await ReportJournalAsync(_directory, cancellationToken);
            if (reported > 0)
            {
                logger.LogWarning("Reported {Count} failure(s) from before this start to the exception log", reported);
            }
        }
        catch (Exception ex)
        {
            // The store may be the very thing that failed; the journal stays for the next start.
            logger.LogError(ex, "Could not report earlier failures to the exception log; they stay in the journal");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        return Task.CompletedTask;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "Unhandled exception; the process is terminating");
            FailureJournal.Write(_directory, "unhandled exception (process terminating)", exception);
        }
    }

    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        args.SetObserved();
        var exception = args.Exception.Flatten().InnerExceptions.FirstOrDefault() ?? args.Exception;
        logger.LogError(exception, "A background task failed and nobody observed it");
        _ = RecordAsync(exception);
    }

    private async Task RecordAsync(Exception exception)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var log = scope.ServiceProvider.GetService<IExceptionLogBll>();
            if (log is not null)
            {
                await log.RecordThrottledAsync(exception, "background task", null, null, UnobservedWindow);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not record the unobserved task failure");
        }
    }
}

/// <summary>
/// A <see cref="BackgroundService"/> whose failure is never silent: an exception from <see cref="ExecuteObservedAsync"/> is
/// logged, recorded in the exception log (throttled) and journaled, then rethrown so the host's own policy still applies.
/// Derive from this for any long-running background work.
/// </summary>
public abstract class ObservedBackgroundService(IServiceScopeFactory scopes, ILogger logger) : BackgroundService
{
    protected abstract Task ExecuteObservedAsync(CancellationToken stoppingToken);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteObservedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // an ordinary shutdown
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Background service {Service} failed", GetType().Name);
            FailureJournal.Write(FailureJournal.DefaultDirectory, $"background service {GetType().Name}", ex);
            try
            {
                using var scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<IExceptionLogBll>() is { } log)
                {
                    await log.RecordThrottledAsync(ex, $"background service: {GetType().Name}", null, null, FailureReporter.UnobservedWindow);
                }
            }
            catch (Exception recordFailure)
            {
                logger.LogDebug(recordFailure, "Could not record the background service failure");
            }

            throw;
        }
    }
}

public static class FailureReportingExtensions
{
    /// <summary>Registers the <see cref="FailureReporter"/> (startup journal report, process-level and unobserved-task failures).</summary>
    public static IServiceCollection AddFailureReporting(this IServiceCollection services) =>
        services.AddHostedService<FailureReporter>();
}
