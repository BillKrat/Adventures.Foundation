using Adventures.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.Data.Postgres;

/// <summary>Makes sure the sign-in tables exist (CREATE ... IF NOT EXISTS: nothing changes where they do). A host without the right to create them logs it and carries on.</summary>
public sealed class IdentitySchemaStartupTask : IStartupTask
{
    public string Name => "identity-schema";

    public bool RunsInLegacyMode => false;

    public bool ContinueOnFailure => true;

    // Resolved here, not injected: a host with no database connection must fail inside the task (logged, start-up carries on), not while the tasks are being created.
    public Task RunAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<IdentitySchemaInitializer>().EnsureAsync(cancellationToken);
}

public static class IdentitySchemaStartupTaskExtensions
{
    public static IServiceCollection AddIdentitySchemaStartupTask(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, IdentitySchemaStartupTask>());
        return services;
    }
}
