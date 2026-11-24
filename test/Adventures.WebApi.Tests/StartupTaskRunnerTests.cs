using Adventures.WebApi;
using Microsoft.Extensions.DependencyInjection;
using Adventures.Common.Interfaces;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>R2 of the host reuse review: features contribute start-up tasks (upgrades, schema creation, first administrator); one runner runs them.</summary>
public sealed class StartupTaskRunnerTests
{
    private sealed class Task1(List<string> log, string name, bool legacy = true, bool carryOn = false, bool fail = false) : IStartupTask
    {
        public string Name => name;
        public bool RunsInLegacyMode => legacy;
        public bool ContinueOnFailure => carryOn;
        public Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            log.Add(name);
            return fail ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }
    }

    private static IServiceProvider Provider(params IStartupTask[] tasks)
    {
        var services = new ServiceCollection();
        foreach (var t in tasks)
        {
            services.AddSingleton(t);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Tasks_run_in_registration_order()
    {
        var log = new List<string>();
        await StartupTaskRunner.RunAsync(Provider(new Task1(log, "a"), new Task1(log, "b")), legacy: false);
        Assert.Equal(["a", "b"], log);
    }

    [Fact]
    public async Task A_task_that_does_not_run_in_legacy_mode_is_skipped_there()
    {
        var log = new List<string>();
        await StartupTaskRunner.RunAsync(Provider(new Task1(log, "schema", legacy: false), new Task1(log, "rights")), legacy: true);
        Assert.Equal(["rights"], log);
    }

    [Fact]
    public async Task A_failure_that_may_continue_is_logged_and_the_rest_run()
    {
        var log = new List<string>();
        await StartupTaskRunner.RunAsync(Provider(new Task1(log, "a", carryOn: true, fail: true), new Task1(log, "b")), legacy: false);
        Assert.Equal(["a", "b"], log);
    }

    [Fact]
    public async Task A_failure_that_may_not_continue_stops_start_up()
    {
        var log = new List<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartupTaskRunner.RunAsync(Provider(new Task1(log, "a", fail: true), new Task1(log, "b")), legacy: false));
        Assert.Equal(["a"], log);
    }
}
