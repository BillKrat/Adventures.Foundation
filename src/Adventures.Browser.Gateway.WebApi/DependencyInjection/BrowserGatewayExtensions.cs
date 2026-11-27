using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Browser.Gateway.WebApi;

public static class BrowserGatewayExtensions
{
    public const string SignInPolicy = "browser-gateway-sign-in";
    public const string ReadPolicy = "browser-gateway-read";
    private const string CorsPolicy = "browser-gateway";

    /// <summary>
    /// The gateway behind <c>api/sites/{domain}</c>, from the <c>BrowserGateway</c> settings: the allowed sites (checked now, so a bad entry stops the
    /// start), a per-visitor rate limit (a site sees only the gateway's address, so its own limit cannot tell visitors apart), and the web origins allowed.
    /// </summary>
    public static IServiceCollection AddBrowserGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(BrowserGatewayOptions.Section).Get<BrowserGatewayOptions>() ?? new BrowserGatewayOptions();
        services.AddSingleton(new SiteDirectory(options));
        services.AddSingleton<SiteForwarder>();
        services.AddHttpClient(SiteForwarder.ClientName).ConfigurePrimaryHttpMessageHandler(SiteForwarder.CreateHandler);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(SignInPolicy, context => PerVisitor(context, options.SignInsPerMinute));
            limiter.AddPolicy(ReadPolicy, context => PerVisitor(context, options.ReadsPerMinute));
        });

        var origins = options.AllowedOrigins.Select(o => o.Trim().TrimEnd('/')).Where(o => o.Length > 0).ToArray();
        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy.WithOrigins(origins).WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type")));
        return services;
    }

    /// <summary>
    /// Refuses a request over plain http unless it is addressed to this machine (a sign-in sends a password), then applies the allowed web origins and the
    /// rate limit. Call before <c>MapControllers</c>, after any trusted-proxy handling.
    /// </summary>
    public static IApplicationBuilder UseBrowserGateway(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps && !IsThisMachine(context.Request.Host.Host))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { title = "Use https.", status = StatusCodes.Status400BadRequest }, context.RequestAborted);
                return;
            }

            await next(context);
        });
        app.UseRouting();
        app.UseCors(CorsPolicy);
        app.UseRateLimiter();
        return app;
    }

    private static bool IsThisMachine(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static RateLimitPartition<string> PerVisitor(HttpContext context, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, perMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
