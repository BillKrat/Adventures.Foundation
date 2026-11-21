using System.Reflection;
using Adventures.MEF;
using Adventures.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF.WebApi;

/// <summary>
/// The rules a plug-in's API parts must keep, checked before anything of the plug-in is registered:
/// every route lives under <c>api/plugins/{key}/</c> (a plug-in cannot shadow or extend the host's own routes), no endpoint is anonymous (a plug-in
/// cannot open a door the host closed), and every tool is tied to the plug-in and calls one of its routes (so the tool disappears with the plug-in,
/// and a tool cannot point at the host's API). A plug-in that breaks a rule is not loaded and the reasons are reported.
/// </summary>
public static class PluginWebRules
{
    /// <summary>The route prefix of a plug-in's API, with a trailing slash.</summary>
    public static string PrefixOf(string pluginKey) => $"api/plugins/{pluginKey}/";

    /// <summary>For <see cref="PluginRegistration.AddPluginDiscovery"/>: the whole check as a validator.</summary>
    public static string? Validator(DiscoveredPlugin plugin, IServiceCollection moduleServices) =>
        Validate(plugin.Definition.Key, plugin.Module.GetType().Assembly, moduleServices);

    /// <summary>All the problems of the plug-in's controllers and tools joined into one sentence, or null when it keeps every rule.</summary>
    public static string? Validate(string pluginKey, Assembly assembly, IServiceCollection moduleServices)
    {
        var problems = new List<string>();
        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or FileLoadException)
        {
            return $"its types could not be read to check them ({ex.GetType().Name})";
        }

        foreach (var type in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            problems.AddRange(CheckController(type, pluginKey));
        }

        problems.AddRange(CheckTools(moduleServices, pluginKey));
        problems.AddRange(CheckWidgets(moduleServices, pluginKey));
        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>The problems of one controller: anonymous endpoints, a missing route, or a route outside the plug-in's prefix.</summary>
    public static IReadOnlyList<string> CheckController(Type controller, string pluginKey)
    {
        var problems = new List<string>();
        var prefix = PrefixOf(pluginKey);
        if (controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
        {
            problems.Add($"{controller.Name} is anonymous ([AllowAnonymous]); plug-in endpoints need a signed-in caller.");
        }

        var classRoute = controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(r => r.Template).FirstOrDefault();
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var templates = action.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template ?? string.Empty)
                .Concat(action.GetCustomAttributes<RouteAttribute>().Select(r => r.Template))
                .ToList();
            if (templates.Count == 0)
            {
                continue;
            }

            if (action.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            {
                problems.Add($"{controller.Name}.{action.Name} is anonymous ([AllowAnonymous]); plug-in endpoints need a signed-in caller.");
            }

            foreach (var template in templates)
            {
                var absolute = template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal);
                if (!absolute && classRoute is null)
                {
                    problems.Add($"{controller.Name} has no route: give it [Route(\"{prefix}...\")].");
                    continue;
                }

                var combined = absolute
                    ? template.TrimStart('~').Trim('/')
                    : string.Join('/', new[] { classRoute!.Trim('/'), template.Trim('/') }.Where(part => part.Length > 0));
                if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{controller.Name}.{action.Name} route '{combined}' must begin with '{prefix}'.");
                }
            }
        }

        return problems.Distinct().ToList();
    }

    /// <summary>The problems of the tools the module registered: tied to this plug-in, calling a route under its prefix, registered as instances so they can be checked.</summary>
    public static IReadOnlyList<string> CheckTools(IServiceCollection moduleServices, string pluginKey)
    {
        var problems = new List<string>();
        var prefix = PrefixOf(pluginKey);
        foreach (var descriptor in moduleServices.Where(d => d.ServiceType == typeof(IAppTool)))
        {
            if (descriptor.ImplementationInstance is not IAppTool tool)
            {
                problems.Add("A tool is registered by a factory or a type; register tools as instances so they can be checked.");
                continue;
            }

            if (!tool.PluginKeys.Contains(pluginKey, StringComparer.Ordinal))
            {
                problems.Add($"The tool '{tool.Name}' is not tied to the plug-in '{pluginKey}', so it would stay offered when the plug-in is switched off (give it the plug-in key).");
            }

            if (!tool.PathTemplate.TrimStart('/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"The tool '{tool.Name}' calls '{tool.PathTemplate}', which is not under '{prefix}'.");
            }
        }

        return problems;
    }

    /// <summary>The problems of the widgets the module registered: owned by this plug-in (so they go with its switch) and keyed <c>{key}.name</c> (so they cannot shadow another widget).</summary>
    public static IReadOnlyList<string> CheckWidgets(IServiceCollection moduleServices, string pluginKey)
    {
        var problems = new List<string>();
        foreach (var descriptor in moduleServices.Where(d => d.ServiceType == typeof(WidgetRegistration)))
        {
            if (descriptor.ImplementationInstance is not WidgetRegistration registration)
            {
                continue;
            }

            if (!string.Equals(registration.PluginKey, pluginKey, StringComparison.Ordinal))
            {
                problems.Add($"The widget '{registration.Definition.Key}' is registered for the plug-in '{registration.PluginKey}', not '{pluginKey}', so it would not go with this plug-in's switch.");
            }

            if (!registration.Definition.Key.StartsWith(pluginKey + ".", StringComparison.Ordinal) || registration.Definition.Key.Length <= pluginKey.Length + 1)
            {
                problems.Add($"The widget key '{registration.Definition.Key}' must start with '{pluginKey}.' (for example '{pluginKey}.note') so it cannot shadow another widget.");
            }
        }

        return problems;
    }
}
