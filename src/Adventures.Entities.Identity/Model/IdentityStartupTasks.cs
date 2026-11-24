using Adventures.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Adventures.Entities;

/// <summary>Improved default rights for roles still at their first shipped values (a seed cannot supersede a stored value); an edited role is left alone.</summary>
public sealed class RoleRightsStartupTask(IEntityDal<Role> roles, ILogger<RoleRightsStartupTask> logger) : IStartupTask
{
    public string Name => "role-rights-upgrade";

    public bool RunsInLegacyMode => true;

    public bool ContinueOnFailure => false;

    public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var upgraded = await RoleRightsUpgrade.ApplyAsync(roles, cancellationToken);
        if (upgraded > 0)
        {
            logger.LogInformation("Upgraded the default rights of {Count} role(s).", upgraded);
        }
    }
}

/// <summary>When nobody is a platform administrator yet, Admin becomes one (never again once someone holds it, so a person switched off on purpose stays off).</summary>
public sealed class PlatformAdministratorStartupTask(PlatformAdministratorBll first, ILogger<PlatformAdministratorStartupTask> logger) : IStartupTask
{
    public string Name => "first-platform-administrator";

    public bool RunsInLegacyMode => true;

    public bool ContinueOnFailure => false;

    public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (await first.EnsureFirstAsync(cancellationToken))
        {
            logger.LogInformation("{UserName} is now the platform administrator (nobody held the switch).", PlatformAdministratorBll.FirstAdministratorUserName);
        }
    }
}

public static class IdentityStartupTaskExtensions
{
    /// <summary>Registers the identity start-up tasks, in the order the host used to run them.</summary>
    public static IServiceCollection AddIdentityStartupTasks(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, RoleRightsStartupTask>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, PlatformAdministratorStartupTask>());
        return services;
    }
}
