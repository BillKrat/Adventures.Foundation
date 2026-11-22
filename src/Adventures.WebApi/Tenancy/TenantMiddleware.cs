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
public sealed class TenantMiddleware(RequestDelegate next, IOptions<TenancyOptions> options, IMemoryCache cache, ILogger<TenantMiddleware> logger, TenantCacheInvalidator? invalidator = null)
{
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, RequestTenant holder, ITenantFirstRun? firstRun = null, IFirstRunSite? firstSite = null)
    {
        var settings = options.Value;
        var host = context.Request.Host.Value ?? string.Empty;
        var current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        if (current.Match == TenantMatch.None && firstSite is not null && !IsExempt(settings, context.Request.Path)
            && await firstSite.TryCreateFirstSiteAsync(host, context.RequestAborted).ConfigureAwait(false))
        {
            // a fresh install: this host is its first site (the store held no tenant at all); forget the "unknown" answer for everyone
            logger.LogInformation("Tenancy: the first request on an empty install came on {Host}; it is now the first site.", host);
            invalidator?.Invalidate();
            cache.Remove(CacheKey(host));
            current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        }

        if (current.Match == TenantMatch.None && firstRun is not null && !string.IsNullOrWhiteSpace(settings.AutoAliasToTenantDomain) && !IsExempt(settings, context.Request.Path)
            && await firstRun.TryAliasAsync(host, settings.AutoAliasToTenantDomain, context.RequestAborted).ConfigureAwait(false))
        {
            // the first run on this address: it now belongs to the tenant the operator named; forget the "unknown" answer for everyone
            logger.LogInformation("Tenancy: the host {Host} was added as an alias of {Domain} (Tenancy:AutoAliasToTenantDomain).", host, settings.AutoAliasToTenantDomain);
            invalidator?.Invalidate();
            cache.Remove(CacheKey(host));
            current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        }

        holder.Set(current);

        if (current.Match == TenantMatch.None && settings.Enforcement == TenancyEnforcement.Enforce && !IsExempt(settings, context.Request.Path))
        {
            await RefuseAsync(context).ConfigureAwait(false);
            return;
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (TenantRequiredException) when (!context.Response.HasStarted)
        {
            // Content is stored per tenant and there is no shared graph, so an address with no tenant has nothing to show (a 404, not a server error).
            context.Response.Clear();
            await RefuseAsync(context).ConfigureAwait(false);
        }
    }

    private static async Task RefuseAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title = "No site is set up for this address.", status = StatusCodes.Status404NotFound }, context.RequestAborted).ConfigureAwait(false);
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
            var entry = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(settings.CacheSeconds) };
            if (invalidator is not null)
            {
                entry.AddExpirationToken(invalidator.Token);
            }

            cache.Set(CacheKey(host), current, entry);
        }

        return current;
    }

    private static string CacheKey(string host) => "tenant:" + host.ToLowerInvariant();

    private static bool IsExempt(TenancyOptions settings, PathString path) =>
        settings.ExemptPaths.Any(exempt => path.Equals(exempt, StringComparison.OrdinalIgnoreCase))
        || settings.ExemptPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
