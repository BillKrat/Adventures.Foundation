using Adventures.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Collaboration.WebApi;

public static class CollaborationWebApiExtensions
{
    /// <summary>
    /// Makes MVC find the collaboration controllers in this library (a controller in a referenced library is not discovered by default).
    /// Call it on the builder returned by <c>AddControllers()</c>, together with the collaboration services behind them.
    /// </summary>
    public static IMvcBuilder AddCollaborationControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var tool in CollaborationAuthoringTools.All)
        {
            builder.Services.AddSingleton<IAppTool>(tool);
        }

        builder.Services.AddCollaborationAdminMenu();

        return builder.AddApplicationPart(typeof(CollaborationWebApiExtensions).Assembly);
    }
}
