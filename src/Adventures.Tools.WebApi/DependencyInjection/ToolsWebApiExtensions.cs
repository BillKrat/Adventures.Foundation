using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.Tools.WebApi;

public static class ToolsWebApiExtensions
{
    /// <summary>
    /// Serves <c>GET /api/tools</c>: makes MVC find the controller, and registers the catalog over every <see cref="IAppTool"/> the features declared
    /// and the default <see cref="IToolSelector"/> (register your own before this call to replace it). Call it after the feature libraries'
    /// <c>AddXxxControllers()</c> calls, which declare their tools, and with <c>AddPlugins()</c> in place.
    /// </summary>
    public static IMvcBuilder AddToolsControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IToolSelector>(_ => new ToolSelector());
        builder.Services.TryAddSingleton(provider => new ToolCatalog(provider.GetServices<IAppTool>()));
        return builder.AddApplicationPart(typeof(ToolsWebApiExtensions).Assembly);
    }
}
