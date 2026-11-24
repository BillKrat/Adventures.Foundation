using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

/// <summary>A feature library's own admin menu entries (R3 of the host reuse review): the feature that owns a screen declares its entry, with the right it needs and the plug-ins that bring it.</summary>
public interface IAdminMenuContributor
{
    IEnumerable<AdminMenuItemDefinition> Items { get; }
}

/// <summary>The simplest contributor: a fixed list.</summary>
public sealed class AdminMenuContribution(IEnumerable<AdminMenuItemDefinition> items) : IAdminMenuContributor
{
    public IEnumerable<AdminMenuItemDefinition> Items { get; } = items.ToList();
}

/// <summary>Every entry the registered features contribute, one catalog for the menu controller.</summary>
public sealed class AdminMenuCatalog
{
    private static readonly string[] KnownSections = ["Dashboard", "Content", "Collaboration", "Custom", "Users", "Settings", "System", "About"];

    /// <summary>Where a section sits: the well-known ones in their usual order, any other after them in the order its first entry was contributed.</summary>
    public static int SectionRank(string section)
    {
        var index = Array.IndexOf(KnownSections, section);
        return index >= 0 ? index : KnownSections.Length;
    }

    public AdminMenuCatalog(IEnumerable<IAdminMenuContributor> contributors)
    {
        All = contributors.SelectMany(c => c.Items).ToList();
        var duplicate = All.GroupBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"The admin menu entry '{duplicate.Key}' is contributed twice.", nameof(contributors));
        }
    }

    public IReadOnlyList<AdminMenuItemDefinition> All { get; }

    /// <summary>The entries without those of plug-ins that are switched off.</summary>
    public IReadOnlyList<AdminMenuItemDefinition> Visible(IReadOnlySet<string> enabledPlugins) =>
        All.Where(item => item.AnyOfPlugins is not { Count: > 0 } keys || keys.Any(enabledPlugins.Contains)).ToList();
}

public static class AdminMenuServiceCollectionExtensions
{
    /// <summary>Registers the menu entries of one feature.</summary>
    public static IServiceCollection AddAdminMenuItems(this IServiceCollection services, params AdminMenuItemDefinition[] items)
    {
        services.AddSingleton<IAdminMenuContributor>(new AdminMenuContribution(items));
        return services;
    }

    /// <summary>Registers a count a feature can show beside a menu entry or on the dashboard.</summary>
    public static IServiceCollection AddAdminCount<TSource>(this IServiceCollection services)
        where TSource : class, IAdminCountSource
    {
        services.AddScoped<IAdminCountSource, TSource>();
        return services;
    }

    /// <summary>Registers the catalog and the cached counts the menu controller and the dashboard read.</summary>
    public static IServiceCollection AddAdminMenuCore(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(sp => new AdminMenuCatalog(sp.GetServices<IAdminMenuContributor>()));
        services.AddScoped<AdminCounts>(sp =>
        {
            var tenant = sp.GetService<IRequestTenant>();
            return new AdminCounts(sp.GetServices<IAdminCountSource>(), sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), () => tenant?.Current.Id ?? string.Empty);
        });
        return services;
    }
}
