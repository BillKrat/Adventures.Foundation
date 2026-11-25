using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Adventures.WebApi;

/// <summary>
/// Decides which tenant a request is for, from the host name the server itself saw (<c>Request.Host</c>, which is what the site bindings on the hosting
/// provider let through) and from nothing else. Origin, Referer and the forwarded-host headers are set by the caller and are never read, so they cannot
/// claim a tenant (threat: host spoofing). A host that matches no tenant is refused with 404 when enforcement is on (threat: silent default), except the
/// infrastructure paths; while a site is being switched over it is only reported. The answer is kept briefly per host so the tenant store is not read on
/// every request.
/// </summary>
public sealed partial class TenantMiddleware(RequestDelegate next, IOptions<TenancyOptions> options, IMemoryCache cache, ILogger<TenantMiddleware> logger, TenantCacheInvalidator? invalidator = null)
{
    /// <summary>
    /// The first-run services (an alias for the first address, the first site of an empty install) are asked for only when a host reaches no tenant: they need the sign-in store, which a
    /// request for a known host must not depend on (an install without that store would answer 500 to every request).
    /// </summary>
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, RequestTenant holder)
    {
        var firstRun = new Lazy<ITenantFirstRun?>(() => context.RequestServices?.GetService<ITenantFirstRun>());
        var firstSite = new Lazy<IFirstRunSite?>(() => context.RequestServices?.GetService<IFirstRunSite>());
        var settings = options.Value;
        var host = context.Request.Host.Value ?? string.Empty;
        var current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        if (current.Match == TenantMatch.None && !IsExempt(settings, context.Request.Path) && firstSite.Value is { } site
            && await AttemptAsync(() => site.TryCreateFirstSiteAsync(host, context.RequestAborted), "creating the first site", host, context.RequestAborted).ConfigureAwait(false))
        {
            // a fresh install: this host is its first site (the store held no tenant at all); forget the "unknown" answer for everyone
            logger.LogInformation("Tenancy: the first request on an empty install came on {Host}; it is now the first site.", host);
            invalidator?.Invalidate();
            cache.Remove(CacheKey(host));
            current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        }

        if (current.Match == TenantMatch.None && !string.IsNullOrWhiteSpace(settings.AutoAliasToTenantDomain) && !IsExempt(settings, context.Request.Path) && firstRun.Value is { } alias
            && await AttemptAsync(() => alias.TryAliasAsync(host, settings.AutoAliasToTenantDomain, context.RequestAborted), "adding the first alias", host, context.RequestAborted).ConfigureAwait(false))
        {
            // the first run on this address: it now belongs to the tenant the operator named; forget the "unknown" answer for everyone
            logger.LogInformation("Tenancy: the host {Host} was added as an alias of {Domain} (Tenancy:AutoAliasToTenantDomain).", host, settings.AutoAliasToTenantDomain);
            invalidator?.Invalidate();
            cache.Remove(CacheKey(host));
            current = await ResolveAsync(settings, host, resolver, context.RequestAborted).ConfigureAwait(false);
        }

        current = await HintedAsync(context, settings, current, host, resolver).ConfigureAwait(false);
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

    /// <summary>
    /// The registered-origin hint (opt in, <see cref="TenancyOriginHint.Registered"/>): when sites share one API address, the browser's Origin (a plain read such as an image carries no Origin, so for GET and HEAD the
    /// Referer's site is used) names the tenant, but only when it is exactly a host name some tenant answers to (the same list that decides which sites may call the API; a <c>www.</c> form too). It is applied only when
    /// the request's own host already reaches a tenant (never to an unknown host, so setup and first-run are untouched), never on the setup and recovery paths, and a token still has to belong to the tenant chosen
    /// (<see cref="TenantTokenMiddleware"/>). A caller outside a browser can claim any registered site, which only shows it that site's public content. The registered names are kept as one set, so an attacker's
    /// made-up origin costs a lookup and nothing is remembered for it.
    /// </summary>
    private async Task<CurrentTenant> HintedAsync(HttpContext context, TenancyOptions settings, CurrentTenant current, string host, ITenantResolver resolver)
    {
        if (settings.OriginHint != TenancyOriginHint.Registered)
        {
            return current;
        }

        // the response depends on who is asking, so no shared cache may hand one site's answer to another
        foreach (var name in new[] { "Origin", "Referer" })
        {
            if (!context.Response.Headers.Vary.ToString().Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.Append("Vary", name);
            }
        }

        if (current.Match is not (TenantMatch.Domain or TenantMatch.Mirror) || IsExempt(settings, context.Request.Path)
            || context.RequestServices?.GetService<ITenantHostSource>() is not { } source)
        {
            return current;
        }

        var via = HintHost(context.Request);
        if (via is null || string.Equals(via, TenantHosts.Normalize(host), StringComparison.Ordinal))
        {
            return current;
        }

        var names = await RegisteredNamesAsync(source, context.RequestAborted).ConfigureAwait(false);
        if (!names.Contains(via) && !(via.StartsWith("www.", StringComparison.Ordinal) && names.Contains(via[4..])))
        {
            return current;
        }

        var hinted = await ResolveAsync(settings, via, resolver, context.RequestAborted, useFallback: false).ConfigureAwait(false);
        return hinted.Match is TenantMatch.Domain or TenantMatch.Mirror ? hinted with { Host = host, Via = via } : current;
    }

    /// <summary>The host name of the site the browser says it is on: the Origin; for GET and HEAD, when there is no Origin, the Referer's site. Anything but a plain http(s) site name gives null.</summary>
    private static string? HintHost(HttpRequest request)
    {
        var plainRead = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
        var origin = request.Headers.Origin.ToString();
        if (request.Headers.Origin.Count > 1)
        {
            return null;
        }

        if (origin.Length > 0)
        {
            return OriginSite().IsMatch(origin) && Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? TenantHosts.Normalize(uri.Host) : null;
        }

        if (plainRead && request.Headers.Referer.Count == 1 && Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var from)
            && (from.Scheme == Uri.UriSchemeHttps || from.Scheme == Uri.UriSchemeHttp) && from.UserInfo.Length == 0 && HostName().IsMatch(from.Host))
        {
            return TenantHosts.Normalize(from.Host);
        }

        return null;
    }

    [GeneratedRegex(@"^https?://[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+(:\d{1,5})?$")]
    private static partial Regex OriginSite();

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+$")]
    private static partial Regex HostName();

    private async Task<HashSet<string>> RegisteredNamesAsync(ITenantHostSource source, CancellationToken cancellationToken)
    {
        const string key = "tenant-origin-hint-names";
        if (cache.TryGetValue(key, out HashSet<string>? kept) && kept is not null)
        {
            return kept;
        }

        var names = new HashSet<string>((await source.AllNamesAsync(cancellationToken).ConfigureAwait(false)).Select(TenantHosts.Normalize), StringComparer.Ordinal);
        var entry = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)) };
        if (invalidator is not null)
        {
            entry.AddExpirationToken(invalidator.Token);
        }

        cache.Set(key, names, entry);
        return names;
    }

    /// <summary>
    /// A first-run step is a convenience for a fresh install, never a reason to fail a request: if it throws (no sign-in store, a store error) it is logged, the host stays unknown and is
    /// refused or served as any unknown host is.
    /// </summary>
    private async Task<bool> AttemptAsync(Func<Task<bool>> step, string what, string host, CancellationToken cancellationToken)
    {
        try
        {
            return await step().ConfigureAwait(false);
        }
        catch (Exception problem) when (problem is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(problem, "Tenancy: {What} for the host {Host} failed; the host stays unknown.", what, host);
            return false;
        }
    }

    private static async Task RefuseAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title = "No site is set up for this address.", status = StatusCodes.Status404NotFound }, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task<CurrentTenant> ResolveAsync(TenancyOptions settings, string host, ITenantResolver resolver, CancellationToken cancellationToken, bool useFallback = true)
    {
        var fallback = useFallback && settings.AllowFallback ? settings.FallbackDomain : null;
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
