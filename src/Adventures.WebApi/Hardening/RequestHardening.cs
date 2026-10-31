// Assessed 2026-10-24 17:51 CT (Claude): automated scan for secrets, injection and unsafe patterns, re-run after the fixes - security findings F-1 to F-3 fixed in this file or in code it uses, covered by tests. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

/// <summary>
/// Request-level defences every Adventures API shares (security review 2026-10-24, F-1 to F-3):
/// a fallback authorization policy so an endpoint that forgets its attribute is closed, not open;
/// a per-client rate limit for the token endpoints, so password guessing across accounts is slowed;
/// and response headers that tell browsers not to sniff, frame or leak the API's responses.
/// </summary>
public static class RequestHardening
{
    /// <summary>Name of the rate-limit policy to put on a token endpoint: <c>[EnableRateLimiting(RequestHardening.TokenPolicy)]</c>.</summary>
    public const string TokenPolicy = "token";

    /// <summary>Configuration key for the number of token requests one client address may make per minute.</summary>
    public const string TokensPerMinuteKey = "RateLimit:TokensPerMinute";

    public const int DefaultTokensPerMinute = 10;

    /// <summary>Name of the rate-limit policy for the anonymous health check: <c>[EnableRateLimiting(RequestHardening.HealthPolicy)]</c>.</summary>
    public const string HealthPolicy = "health";

    /// <summary>Configuration key for the number of health requests one client address may make per minute.</summary>
    public const string HealthPerMinuteKey = "RateLimit:HealthPerMinute";

    public const int DefaultHealthPerMinute = 30;

    /// <summary>A positive configured number, else the default (a zero or garbled setting must not switch the limit off or lock everyone out).</summary>
    public static int ResolveLimit(IConfiguration configuration, string key, int defaultValue)
    {
        return int.TryParse(configuration[key], out var value) && value >= 1 ? value : defaultValue;
    }

    /// <summary>
    /// Registers the fallback policy (an authenticated user is required wherever an endpoint names no policy;
    /// public endpoints must say <c>[AllowAnonymous]</c>) and the <see cref="TokenPolicy"/> rate limit (429 when exceeded).
    /// Call <c>UseRateLimiter()</c> in the pipeline where token endpoints are served.
    /// </summary>
    public static IServiceCollection AddRequestHardening(this IServiceCollection services, IConfiguration configuration)
    {
        var perMinute = ResolveLimit(configuration, TokensPerMinuteKey, DefaultTokensPerMinute);
        var healthPerMinute = ResolveLimit(configuration, HealthPerMinuteKey, DefaultHealthPerMinute);

        services.Configure<AuthorizationOptions>(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(TokenPolicy, context => ClientPartition(context, perMinute));
            options.AddPolicy(HealthPolicy, context => ClientPartition(context, healthPerMinute));
        });

        return services;
    }

    /// <summary>One fixed one-minute window per client address, no queueing (public so the limit can be tested directly).</summary>
    public static RateLimitPartition<string> ClientPartition(HttpContext context, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });

    /// <summary>
    /// Sets the response headers for a JSON API before the rest of the pipeline runs; a later component (for example the
    /// media endpoint's stricter policy) may overwrite one. Strict-Transport-Security is sent over HTTPS outside Development.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool isDevelopment) =>
        app.Use(async (context, next) =>
        {
            SetSecurityHeaders(context, isDevelopment);
            await next(context);
        });

    public static void SetSecurityHeaders(HttpContext context, bool isDevelopment)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
        if (!isDevelopment && context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=15552000";
        }
    }
}
