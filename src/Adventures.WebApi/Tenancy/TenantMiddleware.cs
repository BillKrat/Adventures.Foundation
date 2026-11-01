using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Adventures.WebApi;

/// <summary>
/// Decides which tenant a request is for, from the host name the server itself saw (<c>Request.Host</c>, which is what the site bindings on the hosting
/// provider let through) and from nothing else. Origin, Referer and the forwarded-host headers are set by the caller and are never read, so they cannot
/// claim a tenant (threat: host spoofing). A host that matches no tenant is refused with 404 when enforcement is on (threat: silent default), except the
/// infrastructure paths; while a site is being switched over it is only reported. The answer is kept briefly per host so the tenant store is not read on
/// every request.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next, IOptions<TenancyOptions> options, IMemoryCache cache, ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, RequestTenant holder)
    {
        var settings = options.Value;
        var host = context.Request.Host.Value ?? string.Empty;
        var current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        holder.Set(current);

        if (current.Match == TenantMatch.None && settings.Enforcement == TenancyEnforcement.Enforce && !IsExempt(settings, context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new { title = "No site is set up for this address.", status = StatusCodes.Status404NotFound }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private async Task<CurrentTenant> ResolveAsync(TenancyOptions settings, string host, ITenantResolver resolver, CancellationToken cancellationToken)
    {
        var fallback = settings.AllowFallback ? settings.FallbackDomain : null;
        if (settings.CacheSeconds > 0 && cache.TryGetValue(CacheKey(host), out CurrentTenant? kept) && kept is not null)
        {
            return kept;
        }

        var current = await resolver.ResolveAsync(host, fallback, cancellationToken).ConfigureAwait(false);
        if (current.Match == TenantMatch.None && settings.Enforcement != TenancyEnforcement.Off)
        {
            // runs once per host per cache window, so a busy unknown host is one line a minute, not one per request
            logger.LogWarning("Tenancy: the host {Host} matches no tenant ({Mode}).", host, settings.Enforcement);
        }

        if (settings.CacheSeconds > 0)
        {
            cache.Set(CacheKey(host), current, TimeSpan.FromSeconds(settings.CacheSeconds));
        }

        return current;
    }

    private static string CacheKey(string host) => "tenant:" + host.ToLowerInvariant();

    private static bool IsExempt(TenancyOptions settings, PathString path) =>
        settings.ExemptPaths.Any(exempt => path.Equals(exempt, StringComparison.OrdinalIgnoreCase));
}
