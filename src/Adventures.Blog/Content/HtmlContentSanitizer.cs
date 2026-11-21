using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Blog;

/// <summary>
/// Where HTML from the visual editor is made safe, behind a seam so an owner can switch it off or a developer can replace it. It hands back a plain
/// function for the request, decided once, so code that saves or serves many posts can stay synchronous. The default
/// (<see cref="HtmlContentSanitizer"/>) is today's fixed rules (<see cref="HtmlContent"/>) while the <c>html-sanitizer</c> plug-in is on.
/// </summary>
public interface IHtmlContentSanitizer
{
    Task<Func<string?, string>> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>The identity of the sanitising plug-in, so the Plugins page lists it and the host can switch it off per tenant.</summary>
public static class HtmlSanitizerPlugin
{
    public const string Key = "html-sanitizer";

    public static readonly PluginDefinition Definition = new(
        Key,
        "HTML sanitising",
        "Makes the HTML of posts and pages safe when it is saved and again when it is served: scripts, event handlers, frames, forms and unsafe addresses are removed. "
        + "Switching it off lets HTML through exactly as written (for a site only you write for); anyone who can edit a post could then run script for every visitor.",
        "1.0.0",
        5);
}

/// <summary>
/// The default sanitiser: today's fixed rules while the plug-in is on, HTML as written when an owner has switched it off for the tenant. If the
/// switch cannot be read the answer is to sanitise: a fault never turns safety off.
/// </summary>
public sealed class HtmlContentSanitizer(IServiceProvider services) : IHtmlContentSanitizer
{
    public async Task<Func<string?, string>> GetAsync(CancellationToken cancellationToken = default)
    {
        bool on;
        try
        {
            // resolved here, not injected: a host that stores no plug-in state (the legacy data mode) still gets a working sanitiser, always on
            var plugins = services.GetService<IPlugins>();
            on = plugins is null || await plugins.IsEnabledAsync(HtmlSanitizerPlugin.Key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            on = true;
        }

        return on ? HtmlContent.Sanitize : html => html ?? string.Empty;
    }
}
