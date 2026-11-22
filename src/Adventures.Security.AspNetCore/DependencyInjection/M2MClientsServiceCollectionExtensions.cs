using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Security;

public static class M2MClientsServiceCollectionExtensions
{
    /// <summary>
    /// Binds the machine clients from the <c>M2MClients</c> section, then gives each client the secret named for it (<see cref="M2MClientSecretNames"/>, for example
    /// <c>ClientSecret__ClaudeAuthor</c>). The named secret wins over the older position-based <c>M2MClients__Clients__n__Secret</c>, which keeps working until it is removed. A client with no
    /// secret at all stays inactive.
    /// </summary>
    public static IServiceCollection AddM2MClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<M2MClientsOptions>()
            .Bind(configuration.GetSection(M2MClientsOptions.SectionName))
            .PostConfigure(options =>
            {
                foreach (var client in options.Clients.Where(c => !string.IsNullOrWhiteSpace(c.ClientId)))
                {
                    if (configuration[M2MClientSecretNames.KeyFor(client.ClientId)] is { } named && !string.IsNullOrWhiteSpace(named))
                    {
                        client.Secret = named;
                    }
                }
            });
        return services;
    }
}
