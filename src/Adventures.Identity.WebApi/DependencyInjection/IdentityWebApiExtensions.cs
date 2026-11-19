using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

public static class IdentityWebApiExtensions
{
    /// <summary>
    /// Makes MVC find the identity controllers in this library (a controller in a referenced library is not discovered by default).
    /// Call it on the builder returned by <c>AddControllers()</c>, together with the identity services behind them.
    /// </summary>
    public static IMvcBuilder AddIdentityControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddApplicationPart(typeof(IdentityWebApiExtensions).Assembly);
    }
}
