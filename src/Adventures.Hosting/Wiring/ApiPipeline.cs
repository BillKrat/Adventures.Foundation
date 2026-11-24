// Startup composition: the HTTP request pipeline and its endpoints (moved out of Program.cs, no behaviour change).
using Adventures.Common.Diagnostics;
using Adventures.Data;
using Adventures.Data.Postgres;
using Adventures.Identity.Postgres;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.Identity;
using Adventures.Ioc.Extensions;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Adventures.Blog;
using Adventures.Blog.Legacy;
using Adventures.Blog.WebApi;
using Adventures.Collaboration.WebApi;
using Adventures.Diagnostics.WebApi;
using Adventures.Identity.WebApi;
using Adventures.Blog.Postgres;
using Adventures.Collaboration;
using Adventures.MEF;

namespace Adventures.Hosting;

internal static class AppPipeline
{
    public static void UseAppPipeline(this WebApplication app, ApiHostOptions options)
    {
        const string AngularCorsPolicy = AppPlatformServices.AngularCorsPolicy;

        // The shared front half (proxies, correlation id, headers, request log, https, CORS, rate limit, the one exception handler): Adventures.WebApi.HostPipeline.
        app.UseAdventuresPreamble(AngularCorsPolicy);
        // Single-site mode: the client build in the web root is served by this host (assets and the page need no tenant).
        var client = ClientSite.TryLoad(app.Environment.WebRootPath, app.Configuration);
        client?.Use(app);

        // After the exception handler and the rate limiter, before authentication: a host that matches no tenant never reaches the controllers when enforcement is on.
        app.UseTenancy();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        options.MapDefaultEndpoints?.Invoke(app);

        app.UseAuthentication();
        app.UseTenantTokenCheck();
        app.UseActingTenant();
        app.UsePasswordChangeRequired();
        app.UseAuthorization();

        app.MapControllers();

        if (app.Environment.IsDevelopment())
        {
            // Test aid, not a feature: throws on purpose so the exception log, the header warning and the Exceptions screen
            // can be exercised end to end. Development only (not mapped in production), administrators only.
            static IResult ThrowDeliberately() => throw new InvalidOperationException("Deliberate test exception from /api/dev/throw.");
            app.MapGet("/api/dev/throw", ThrowDeliberately)
                .RequireAuthorization(WellKnownClaims.TenantAdminPolicy)
                .ExcludeFromDescription();
        }

        // SmarterASP's free-SSL request (issue and renewal) requires the domain root to return HTTP 200 (with the client served here the page itself answers 200).
        // With the client served here there is no banner: routing has already chosen an endpoint for "/", and the static files only answer a request that has none.
        if (client is null)
        {
            app.MapGet("/", () => Results.Ok(new { service = options.ServiceName, status = "ok" })).AllowAnonymous()
                .ExcludeFromDescription();
        }

        // Last: client routes fall back to the page, everything else stays as it was.
        client?.MapFallback(app);
    }
}
