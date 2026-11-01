using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Adventures.WebApi;

/// <summary>
/// Refuses a token that was signed in for another tenant than the one this request is for (threat: token reuse across tenants), with 403. Put it right
/// after authentication. A token with no tenant claim (a machine client, or a token issued before tenancy) is not checked, and neither is an anonymous
/// caller or a request whose host matched no tenant.
/// </summary>
public sealed class TenantTokenMiddleware(RequestDelegate next, IOptions<TenancyOptions> options)
{
    public async Task InvokeAsync(HttpContext context, RequestTenant holder)
    {
        var claimed = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(WellKnownClaims.Tenant)?.Value : null;
        var current = holder.Current;
        if (options.Value.Enforcement != TenancyEnforcement.Off
            && !string.IsNullOrWhiteSpace(claimed)
            && current.Match != TenantMatch.None
            && !string.Equals(Normalize(claimed), Normalize(current.Domain), StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new { title = "This sign-in belongs to another site. Sign in again here.", status = StatusCodes.Status403Forbidden }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static string Normalize(string? domain) => (domain ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
}
