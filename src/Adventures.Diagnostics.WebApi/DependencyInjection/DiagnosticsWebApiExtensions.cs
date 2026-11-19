using Microsoft.AspNetCore.Mvc;

namespace Adventures.Diagnostics.WebApi;

public static class DiagnosticsWebApiExtensions
{
    /// <summary>
    /// Makes MVC find the diagnostics controllers in this library (a controller in a referenced library is not discovered by default).
    /// Call it on the builder returned by <c>AddControllers()</c>, together with the diagnostics services behind them.
    /// </summary>
    public static IMvcBuilder AddDiagnosticsControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddApplicationPart(typeof(DiagnosticsWebApiExtensions).Assembly);
    }
}
