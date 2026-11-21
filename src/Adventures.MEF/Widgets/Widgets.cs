using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF;

/// <summary>The four places a widget can be put on a page: above the content, beside it on either side, and below it. A phone shows only header and footer.</summary>
public static class WidgetZones
{
    public const string Header = "header";
    public const string Left = "left";
    public const string Right = "right";
    public const string Footer = "footer";

    public static readonly IReadOnlyList<string> All = [Header, Left, Right, Footer];

    /// <summary>True for one of the four zone names (ignoring case).</summary>
    public static bool IsValid(string? zone) => zone is not null && All.Contains(zone.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The zone in its canonical lower-case form, or null when it is not one of the four.</summary>
    public static string? Normalise(string? zone) => IsValid(zone) ? zone!.Trim().ToLowerInvariant() : null;
}

/// <summary>What a widget is, as data: its key, the name shown on the Widgets screen, and the settings an owner fills in for each placement.</summary>
public sealed record WidgetDefinition(string Key, string Title, string Description, IReadOnlyList<PluginSettingDefinition>? Settings = null);

/// <summary>What a widget is asked to render: the zone it sits in, its settings for this placement (defaults filled in) and the tenant it is shown for.</summary>
public sealed record WidgetContext(string Zone, IReadOnlyDictionary<string, string> Settings, string? Tenant);

/// <summary>
/// A widget: code that returns a piece of HTML for a zone, so a C# developer needs no Angular. The host passes the HTML through the sanitiser plug-in,
/// gives the widget a time limit, and leaves it out of the page (and logs it) if it throws or hangs. A widget belongs to a plug-in and is offered only
/// while that plug-in is on for the tenant.
/// </summary>
public interface IWidget
{
    Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken);
}

/// <summary>One widget a plug-in registered: which plug-in owns it, what it is, and its type (created per request).</summary>
public sealed record WidgetRegistration(string PluginKey, WidgetDefinition Definition, Type WidgetType);

public static class WidgetServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="TWidget"/> as a widget of the plug-in <paramref name="pluginKey"/> (call it from the plug-in module's <c>ConfigureServices</c>).</summary>
    public static IServiceCollection AddWidget<TWidget>(this IServiceCollection services, string pluginKey, WidgetDefinition definition)
        where TWidget : class, IWidget
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginKey);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
        services.AddScoped<TWidget>();
        services.AddSingleton(new WidgetRegistration(pluginKey, definition, typeof(TWidget)));
        return services;
    }
}
