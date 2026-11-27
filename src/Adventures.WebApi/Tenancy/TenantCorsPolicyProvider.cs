using System.Text.RegularExpressions;
using Adventures.Entities;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adventures.WebApi;

/// <summary>
/// The CORS policy of the API, built from data: a browser site may call the API when an administrator registered it (a tenant's domain, its <c>www.</c> host or
/// an alias such as the host's temporary address), plus the fixed origins from configuration (development, an existing install). The answer is kept briefly so the
/// tenant store is not read on every request, and when the store cannot be read only the fixed origins apply (fail closed). A real domain is trusted over https
/// only; a host's temporary address (<c>.atempurl.com</c>, <c>.dtempurl.com</c>, <c>.ftempurl.com</c>) also over http because that site has no certificate until a domain is attached.
/// No wildcard, no cookies (the API takes bearer tokens only). An allowed origin lets a browser call the API; it never decides the tenant (that is the host the
/// server saw, see <see cref="TenantMiddleware"/>) and never grants a right.
/// </summary>
public sealed partial class TenantCorsPolicyProvider(
    IServiceScopeFactory scopes,
    IMemoryCache cache,
    ILogger<TenantCorsPolicyProvider> logger,
    string policyName,
    IReadOnlyCollection<string> fixedOrigins,
    int cacheSeconds,
    TenantCacheInvalidator? invalidator = null) : ICorsPolicyProvider
{
    private const string CacheKey = "tenant-cors-origins";
    private static readonly string[] TemporarySuffixes = [".atempurl.com", ".dtempurl.com", ".ftempurl.com"];

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex HostName();

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? requestedPolicyName)
    {
        if (!string.Equals(requestedPolicyName ?? policyName, policyName, StringComparison.Ordinal))
        {
            return null;
        }

        // Not the request's own cancellation: the list is shared by every visitor, so reading it must not stop, or fail closed, because one of them left.
        var allowed = await AllowedAsync(CancellationToken.None).ConfigureAwait(false);
        return new CorsPolicyBuilder()
            .SetIsOriginAllowed(allowed.Contains)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)
            .Build();
    }

    private async Task<HashSet<string>> AllowedAsync(CancellationToken cancellationToken)
    {
        if (cacheSeconds > 0 && cache.TryGetValue(CacheKey, out HashSet<string>? kept) && kept is not null)
        {
            return kept;
        }

        var allowed = new HashSet<string>(fixedOrigins, StringComparer.OrdinalIgnoreCase);
        var ttl = cacheSeconds;
        try
        {
            using var scope = scopes.CreateScope();
            foreach (var name in await scope.ServiceProvider.GetRequiredService<ITenantHostSource>().AllNamesAsync(cancellationToken).ConfigureAwait(false))
            {
                Add(allowed, name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fail closed: only the fixed origins, and look again soon
            logger.LogWarning(ex, "CORS: the registered sites could not be read; only the configured origins are allowed for now.");
            ttl = Math.Min(cacheSeconds, 10);
        }

        if (ttl > 0)
        {
            var entry = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttl) };
            if (invalidator is not null)
            {
                entry.AddExpirationToken(invalidator.Token);
            }

            cache.Set(CacheKey, allowed, entry);
        }

        return allowed;
    }

    private static void Add(HashSet<string> allowed, string? raw)
    {
        var name = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (!HostName().IsMatch(name))
        {
            return; // never turn a malformed value into a trusted origin
        }

        allowed.Add($"https://{name}");
        if (!name.StartsWith("www.", StringComparison.Ordinal))
        {
            allowed.Add($"https://www.{name}");
        }

        if (TemporarySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
        {
            allowed.Add($"http://{name}");
        }
    }
}
