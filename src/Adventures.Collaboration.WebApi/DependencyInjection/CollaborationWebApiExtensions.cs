using Microsoft.AspNetCore.Mvc;

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
        return builder.AddApplicationPart(typeof(CollaborationWebApiExtensions).Assembly);
    }
}
