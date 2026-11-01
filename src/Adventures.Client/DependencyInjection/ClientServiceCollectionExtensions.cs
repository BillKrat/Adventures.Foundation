using Adventures.Entities;
using Adventures.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Client;

public static class ClientServiceCollectionExtensions
{
    /// <summary>
    /// The whole client composition: one shared <see cref="HttpClient"/> aimed at <paramref name="apiBaseAddress"/>, the ambient principal
    /// (<see cref="ICurrentPrincipal"/>) and the <see cref="ApiSession"/> that fills it. Register each remote entity list with
    /// <see cref="AddRemotePresenter{TEntity}"/>.
    /// </summary>
    public static IServiceCollection AddAdventuresClient(this IServiceCollection services, Uri apiBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(apiBaseAddress);
        // The sign-in sends a password: plain http is allowed only to this machine (development), never across a network.
        if (apiBaseAddress.Scheme != Uri.UriSchemeHttps && !(apiBaseAddress.Scheme == Uri.UriSchemeHttp && apiBaseAddress.IsLoopback))
        {
            throw new ArgumentException("The API address must use https (plain http is allowed only for localhost).", nameof(apiBaseAddress));
        }

        services.AddSingleton(_ => new HttpClient { BaseAddress = apiBaseAddress, Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<AmbientPrincipal>();
        services.AddSingleton<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
        services.AddSingleton<ApiSession>();
        return services;
    }

    /// <summary>Binds <see cref="IEntityPresenter{TEntity}"/> to the API list at <paramref name="route"/> (for example <c>api/users</c>).</summary>
    public static IServiceCollection AddRemotePresenter<TEntity>(this IServiceCollection services, string route)
        where TEntity : DynamicEntity =>
        services.AddSingleton<IEntityPresenter<TEntity>>(sp => new HttpEntityPresenter<TEntity>(sp.GetRequiredService<HttpClient>(), route));
}
