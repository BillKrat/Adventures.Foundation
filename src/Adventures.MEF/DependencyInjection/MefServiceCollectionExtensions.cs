using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.MEF;

public static class MefServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IPlugins"/>. The host also registers where plug-in states are stored (an entity repository for <see cref="Plugin"/>).</summary>
    public static IServiceCollection AddPlugins(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IPlugins, Plugins>();
        services.TryAddScoped<Adventures.Entities.IEntityBll<Plugin>>(p => new Adventures.Entities.EntityBll<Plugin>(p.GetRequiredService<Adventures.Entities.IEntityDal<Plugin>>()));
        return services;
    }

    /// <summary>Declares a plug-in a feature contributes; the Plugins page lists it and the host can switch it off.</summary>
    public static IServiceCollection AddPlugin(this IServiceCollection services, PluginDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        return services;
    }
}
