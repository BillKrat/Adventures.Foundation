using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Adventures.WebApi;

/// <summary>The front half of every host's request pipeline, in one place (moved from the API host, R1 of the host reuse review): trusted proxies, correlation id, security headers, request logging, https redirect that spares "/", CORS, rate limiter, the one exception handler.</summary>
public static class HostPipeline
{
    /// <summary>Applies the shared preamble. Call it first; a host then adds its own tenancy, authentication and endpoints after it.</summary>
    /// <param name="corsPolicy">The named CORS policy the host registered; none for a host that no browser calls (the MCP gateway).</param>
    public static void UseAdventuresPreamble(this WebApplication app, string? corsPolicy = null)
    {
        // Before everything: behind a CDN or proxy the visitor address and scheme come from the listed proxies only (Proxy:Trusted, off by default), so the redirect and the rate limits see the visitor.
        app.UseTrustedProxies(app.Configuration);
        // First in the pipeline: everything after it (request logging, the exception handler, controllers) logs under the id.
        app.UseCorrelationId();
        app.UseSecurityHeaders(app.Environment.IsDevelopment());
        app.Use(async (context, next) =>
        {
            using (Serilog.Context.LogContext.PushProperty(CorrelationIdMiddleware.LogPropertyName, CorrelationIdMiddleware.Get(context)))
            {
                await next(context);
            }
        });
        // Each request line names the client address (the visitor behind a trusted proxy, see Proxy:Trusted), so visitors can be told apart in the log.
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms from {ClientIp}";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) => diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        });

        // UseCors must come before any Map* calls so it runs ahead of terminal middleware and can attach CORS headers.
        // The root stays reachable over plain HTTP: SmarterASP's free-SSL preflight requests it without following redirects, so redirecting "/" would fail issue and renewal.
        app.UseWhen(context => !context.Request.Path.Equals("/"), branch => branch.UseHttpsRedirection());
        if (corsPolicy is not null)
        {
            app.UseCors(corsPolicy);
        }

        app.UseRateLimiter();

        // One handler in every environment, so what a developer sees is what production does: the full exception goes to the log file and the exception log,
        // the client gets a shielded body with a reference (see UnhandledExceptionHandler). A visitor who left is not a failure (see AbandonedRequests).
        app.UseShieldedErrors();
    }
}
