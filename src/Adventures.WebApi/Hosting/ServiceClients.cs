using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Adventures.WebApi;

/// <summary>The one recipe for an outgoing HTTP client to another of our sites (R4 of the host reuse review): base address from configuration at first use, a timeout, the correlation id on every call.</summary>
public static class ServiceClients
{
    /// <param name="noRetries">True for a probe: the default resilience handler would turn one failure into seconds of waiting.</param>
    public static IHttpClientBuilder AddServiceClient(this IServiceCollection services, string name, Func<IServiceProvider, Uri> baseAddress, TimeSpan timeout, bool noRetries = false)
    {
        var builder = services.AddHttpClient(name, (provider, client) =>
        {
            client.BaseAddress = baseAddress(provider);
            client.Timeout = timeout;
        });
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental; it is the supported way to opt one client out.
        if (noRetries)
        {
            builder.RemoveAllResilienceHandlers();
        }
#pragma warning restore EXTEXP0001
        return builder.AddCorrelationIdHandler();
    }
}
