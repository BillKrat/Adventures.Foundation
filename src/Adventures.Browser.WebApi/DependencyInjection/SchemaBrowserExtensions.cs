using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Browser.WebApi;

public static class SchemaBrowserExtensions
{
    /// <summary>
    /// Switches the schema browser routes on. A host that does not call this answers 404 on them, even though the controller is in its dependencies.
    /// The host also registers the <c>ISchemaCatalogSource</c> of its store and the rights authorization.
    /// </summary>
    public static IServiceCollection AddSchemaBrowser(this IServiceCollection services) => services.AddSingleton<SchemaBrowserSwitch>();
}

/// <summary>Registered by <see cref="SchemaBrowserExtensions.AddSchemaBrowser"/>; its presence is the switch.</summary>
public sealed class SchemaBrowserSwitch;

/// <summary>Answers 404, before the rights check, while the browser is switched off.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SchemaBrowserSwitchedOnAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.RequestServices.GetService<SchemaBrowserSwitch>() is null)
        {
            context.Result = new NotFoundResult();
        }
    }
}
