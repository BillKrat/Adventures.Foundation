using Adventures.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Adventures.WebApi;

/// <summary>
/// Keeps a setup token (see <see cref="WellKnownClaims.Setup"/>) on the setup API. A setup token is issued to a platform administrator on a host that reaches no tenant; anywhere else it would
/// be an authenticated caller with no tenant, and a name that could be mistaken for a person of the tenant being called. Every other route answers 403 to it. Put it right after authentication.
/// </summary>
public sealed class SetupTokenGuardMiddleware(RequestDelegate next)
{
    public const string SetupPath = "/api/setup";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(claim => claim.Type == WellKnownClaims.Setup)
            && !IsSetupApi(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new { title = "A setup token works for the setup only.", status = StatusCodes.Status403Forbidden }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>The setup path itself or something under it, never a neighbour such as /api/setupx.</summary>
    internal static bool IsSetupApi(PathString path) =>
        path.StartsWithSegments(SetupPath, StringComparison.OrdinalIgnoreCase);
}

public static class SetupTokenGuardExtensions
{
    public static IApplicationBuilder UseSetupTokenGuard(this IApplicationBuilder app) => app.UseMiddleware<SetupTokenGuardMiddleware>();
}
