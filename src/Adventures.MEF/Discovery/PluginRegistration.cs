using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF;

/// <summary>A discovered plug-in that is now part of the host.</summary>
public sealed record LoadedPlugin(string Key, string Title, string Version, string Source);

/// <summary>What discovery found at start-up: the plug-ins that loaded and every file or plug-in that did not, with the reason. Shown to administrators.</summary>
public sealed record PluginLoadReport(IReadOnlyList<LoadedPlugin> Loaded, IReadOnlyList<PluginLoadFailure> Failures);

/// <summary>Puts discovered plug-ins into the host: their definitions (so the Plugins page lists them and an administrator can switch them off) and the services they register.</summary>
public static class PluginRegistration
{
    /// <summary>Configuration section: <c>Plugins:Directory</c> (a folder of plug-in DLLs; unset means no scan) and <c>Plugins:RequireApproval</c> (default true).</summary>
    public const string DirectorySetting = "Plugins:Directory";

    public const string RequireApprovalSetting = "Plugins:RequireApproval";

    /// <summary>
    /// Finds plug-ins in <paramref name="hostAssemblies"/> and in the configured plug-in directory and registers them. Call once at start-up, before the
    /// service provider is built. A plug-in that fails to load or to configure is reported in <see cref="PluginLoadReport"/> and adds nothing.
    /// </summary>
    public static IServiceCollection AddPluginDiscovery(this IServiceCollection services, IConfiguration configuration, IEnumerable<Assembly> hostAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hostAssemblies);

        var directory = configuration[DirectorySetting];
        if (!string.IsNullOrWhiteSpace(directory) && !Path.IsPathRooted(directory))
        {
            directory = Path.Combine(AppContext.BaseDirectory, directory);
        }

        var requireApproval = !bool.TryParse(configuration[RequireApprovalSetting], out var parsed) || parsed;
        var result = PluginDiscovery.Discover(hostAssemblies, new PluginDirectoryOptions(directory, requireApproval));
        services.AddSingleton(Register(services, result));
        return services;
    }

    /// <summary>Registers each discovered plug-in. A module that throws while configuring contributes nothing (its services are collected apart and dropped).</summary>
    public static PluginLoadReport Register(IServiceCollection services, PluginDiscoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(result);

        var loaded = new List<LoadedPlugin>();
        var failures = result.Failures.ToList();
        foreach (var plugin in result.Plugins)
        {
            var own = new ServiceCollection();
            try
            {
                plugin.Module.ConfigureServices(own);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add(new PluginLoadFailure(plugin.Source, $"The plug-in '{plugin.Definition.Key}' failed to configure ({ex.GetType().Name}: {ex.Message})."));
                continue;
            }

            services.AddPlugin(plugin.Definition);
            foreach (var descriptor in own)
            {
                services.Add(descriptor);
            }

            loaded.Add(new LoadedPlugin(plugin.Definition.Key, plugin.Definition.Title, plugin.Definition.Version, plugin.Source));
        }

        return new PluginLoadReport(loaded, failures);
    }
}
