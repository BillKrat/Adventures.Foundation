using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventures.Blog;

/// <summary>A widget that can be placed: which plug-in owns it and what it is.</summary>
public sealed record WidgetInfo(string PluginKey, WidgetDefinition Definition);

/// <summary>Limits applied to every widget.</summary>
public sealed class WidgetRenderOptions
{
    /// <summary>The largest sanitised answer kept (characters); a larger one is dropped, because cutting HTML breaks its markup.</summary>
    public const int DefaultMaxLength = 100_000;

    /// <summary>The longest a widget may take to render before it is left out.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);

    public int MaxLength { get; set; } = DefaultMaxLength;
}

/// <summary>Finds the widgets that can be placed and renders one for a zone, the way the host allows (see <see cref="IWidget"/>).</summary>
public interface IWidgetRenderer
{
    /// <summary>The widgets of the plug-ins that are on for this tenant.</summary>
    Task<IReadOnlyList<WidgetInfo>> AvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>The widget's sanitised HTML for the zone, or null when the widget or zone is unknown, its plug-in is off, or it failed, hung or answered too much.</summary>
    Task<string?> RenderAsync(string widgetKey, string zone, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
}

public static class WidgetServiceCollectionExtensions
{
    /// <summary>Registers the widget renderer. Safe to call more than once.</summary>
    public static IServiceCollection AddWidgets(this IServiceCollection services, Action<WidgetRenderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new WidgetRenderOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped<IWidgetRenderer, WidgetRenderer>();
        return services;
    }
}

public sealed class WidgetRenderer(
    IServiceProvider services,
    IEnumerable<WidgetRegistration> registrations,
    WidgetRenderOptions options,
    ILogger<WidgetRenderer>? logger = null) : IWidgetRenderer
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<IReadOnlyList<WidgetInfo>> AvailableAsync(CancellationToken cancellationToken = default) =>
        (await EnabledRegistrationsAsync(cancellationToken).ConfigureAwait(false)).Select(r => new WidgetInfo(r.PluginKey, r.Definition)).ToList();

    public async Task<string?> RenderAsync(string widgetKey, string zone, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var normalised = WidgetZones.Normalise(zone);
        if (normalised is null)
        {
            return null;
        }

        var registration = (await EnabledRegistrationsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => string.Equals(r.Definition.Key, widgetKey, StringComparison.Ordinal));
        if (registration is null)
        {
            return null;
        }

        try
        {
            var widget = (IWidget)services.GetRequiredService(registration.WidgetType);
            var context = new WidgetContext(normalised, WithDefaults(registration.Definition, settings), services.GetService<IPluginEventContext>()?.Capture(services).Tenant);
            var html = await widget.RenderAsync(context, cancellationToken).WaitAsync(options.Timeout, cancellationToken).ConfigureAwait(false);
            var sanitize = services.GetService<IHtmlContentSanitizer>() is { } sanitizer
                ? await sanitizer.GetAsync(cancellationToken).ConfigureAwait(false)
                : HtmlContent.Sanitize;
            var clean = sanitize(html);
            if (clean.Length > options.MaxLength)
            {
                _logger.LogWarning("The widget {Widget} of plug-in {Plugin} answered {Length} characters, more than {Max}, and was left out", widgetKey, registration.PluginKey, clean.Length, options.MaxLength);
                return null;
            }

            return clean;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("The widget {Widget} of plug-in {Plugin} timed out and was left out", widgetKey, registration.PluginKey);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("The widget {Widget} of plug-in {Plugin} failed and was left out: {Reason}", widgetKey, registration.PluginKey, ex.Message);
            return null;
        }
    }

    /// <summary>The settings the widget declared: the given value, or the declared default. Anything not declared is dropped.</summary>
    private static Dictionary<string, string> WithDefaults(WidgetDefinition definition, IReadOnlyDictionary<string, string> given) =>
        (definition.Settings ?? []).ToDictionary(s => s.Name, s => given.TryGetValue(s.Name, out var value) ? value : s.Default, StringComparer.Ordinal);

    /// <summary>The registrations whose plug-in is on for this tenant; if the switches cannot be read, none (unknown means off).</summary>
    private async Task<IReadOnlyList<WidgetRegistration>> EnabledRegistrationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (services.GetService<IPlugins>() is { } plugins)
            {
                var on = await plugins.EnabledKeysAsync(cancellationToken).ConfigureAwait(false);
                return registrations.Where(r => on.Contains(r.PluginKey)).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Plug-in switches could not be read, so no widget was offered: {Reason}", ex.Message);
            return [];
        }

        return registrations.ToList();
    }
}
