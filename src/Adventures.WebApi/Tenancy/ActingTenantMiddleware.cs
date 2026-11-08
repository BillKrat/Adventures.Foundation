using Adventures.Entities;
using Microsoft.AspNetCore.Http;

namespace Adventures.WebApi;

/// <summary>
/// Lets a signed-in person work in another tenant than the one their host names (the tenant switcher). The client names the tenant it wants in the
/// <see cref="HeaderName"/> header; the server decides: the person must be allowed to act there (a tenant they belong to, or any tenant for a platform
/// administrator, see <see cref="IActingTenants"/>), otherwise the request is refused with 403. Anonymous callers and machine clients that are not people never get
/// to choose. Put it right after <c>UseTenantTokenCheck</c> (after authentication): the host still decides the tenant of everything else, and the token
/// check has already run against the host's tenant. The rights of the request are then those the person holds in the chosen tenant.
/// Threat addressed: acting in, or reading the data of, a tenant one does not belong to.
/// </summary>
public sealed class ActingTenantMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Acting-Tenant";

    public async Task InvokeAsync(HttpContext context, RequestTenant holder, IActingTenants acting)
    {
        var wanted = context.Request.Headers[HeaderName].ToString().Trim();
        if (context.User.Identity?.IsAuthenticated == true && wanted.Length > 0 && !string.Equals(wanted, holder.Current.Id, StringComparison.Ordinal))
        {
            var allowed = await acting.AllowedAsync(context.User.Identity.Name ?? string.Empty, wanted, context.RequestAborted).ConfigureAwait(false);
            if (allowed is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new { title = "You cannot act in that tenant.", status = StatusCodes.Status403Forbidden }, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            holder.Set(new CurrentTenant(allowed.Id, allowed.Name, allowed.Domain, holder.Current.Host, TenantMatch.Acting));
        }

        await next(context).ConfigureAwait(false);
    }
}
