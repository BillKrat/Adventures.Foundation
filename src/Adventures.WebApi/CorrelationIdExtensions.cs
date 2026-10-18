using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

public static class CorrelationIdExtensions
{
    /// <summary>Registers what <see cref="CorrelationIdHandler"/> needs. Call once per site.</summary>
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();
        return services;
    }

    /// <summary>Adds the outbound id header to a named or typed HttpClient.</summary>
    public static IHttpClientBuilder AddCorrelationIdHandler(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<CorrelationIdHandler>();

    /// <summary>Put this first in the pipeline, before request logging and the exception handler.</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
