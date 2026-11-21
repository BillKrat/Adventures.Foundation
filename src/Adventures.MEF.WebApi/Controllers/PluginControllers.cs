using System.Reflection;
using Adventures.MEF;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF.WebApi;

/// <summary>Answers 404 for a plug-in's endpoint while that plug-in is switched off for the tenant, so a disabled plug-in has no API (its data is kept).</summary>
public sealed class PluginEnabledFilter(string pluginKey) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.RequestServices.GetService<IPlugins>() is { } plugins
            && !(await plugins.EnabledKeysAsync(context.HttpContext.RequestAborted).ConfigureAwait(false)).Contains(pluginKey))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next().ConfigureAwait(false);
    }
}

/// <summary>Gives every controller that came from a plug-in the enabled check and a signed-in-caller requirement, on top of the rules checked at load.</summary>
public sealed class PluginControllerConvention(IReadOnlyDictionary<Assembly, string> keysByAssembly) : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (!keysByAssembly.TryGetValue(controller.ControllerType.Assembly, out var key))
        {
            return;
        }

        controller.Filters.Add(new AuthorizeFilter());
        controller.Filters.Add(new PluginEnabledFilter(key));
    }
}

public static class PluginControllersExtensions
{
    /// <summary>
    /// Makes MVC find the controllers of the plug-ins that were accepted (call it after <c>AddPluginDiscovery</c>, on the builder returned by
    /// <c>AddControllers()</c>), and puts each under its plug-in's switch and the signed-in-caller requirement.
    /// </summary>
    public static IMvcBuilder AddPluginControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var loaded = builder.Services.Where(d => d.ServiceType == typeof(PluginAssemblies)).Select(d => d.ImplementationInstance).OfType<PluginAssemblies>()
            .SelectMany(a => a.Loaded).ToList();
        foreach (var plugin in loaded)
        {
            builder.AddApplicationPart(plugin.Assembly);
        }

        var map = loaded.ToDictionary(l => l.Assembly, l => l.Key);
        builder.Services.Configure<MvcOptions>(options => options.Conventions.Add(new PluginControllerConvention(map)));
        return builder;
    }
}
