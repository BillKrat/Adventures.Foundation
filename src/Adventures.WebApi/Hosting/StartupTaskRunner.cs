using Adventures.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Adventures.WebApi;

/// <summary>Runs every registered <see cref="IStartupTask"/> in registration order, in one scope (so a task may be a scoped service) (R2 of the host reuse review).</summary>
public static class StartupTaskRunner
{
    public static async Task RunAsync(IServiceProvider services, bool legacy, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
        {
            if (legacy && !task.RunsInLegacyMode)
            {
                continue;
            }

            try
            {
                await task.RunAsync(scope.ServiceProvider, cancellationToken);
            }
            catch (Exception ex) when (task.ContinueOnFailure)
            {
                Log.Warning(ex, "Start-up task {Task} failed; continuing.", task.Name);
            }
        }
    }
}
