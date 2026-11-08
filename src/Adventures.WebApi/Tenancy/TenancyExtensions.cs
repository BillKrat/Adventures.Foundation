using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

public static class TenancyExtensions
{
    /// <summary>
    /// Registers the tenancy settings (section <c>Tenancy</c>), the short-lived cache and the per-request <see cref="IRequestTenant"/>. The development fallback
    /// is switched on only when <paramref name="isDevelopment"/> is true; configuration cannot turn it on in a deployed site. The host also registers
    /// <c>ITenantResolver</c>.
    /// </summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName))
            .PostConfigure(options => options.AllowFallback = isDevelopment);
        services.AddMemoryCache();
        services.AddScoped<RequestTenant>();
        services.AddScoped<IRequestTenant>(provider => provider.GetRequiredService<RequestTenant>());
        return services;
    }

    /// <summary>Decides the tenant of each request from the host the server saw. Early in the pipeline, after the correlation id and the exception handler.</summary>
    public static IApplicationBuilder UseTenancy(this IApplicationBuilder app) => app.UseMiddleware<TenantMiddleware>();

    /// <summary>Refuses a token signed in for another tenant. Straight after <c>UseAuthentication</c>.</summary>
    public static IApplicationBuilder UseTenantTokenCheck(this IApplicationBuilder app) => app.UseMiddleware<TenantTokenMiddleware>();

    /// <summary>Lets a signed-in person act in another tenant they may work in (the <c>X-Acting-Tenant</c> header). Straight after <c>UseTenantTokenCheck</c>.</summary>
    public static IApplicationBuilder UseActingTenant(this IApplicationBuilder app) => app.UseMiddleware<ActingTenantMiddleware>();
}
