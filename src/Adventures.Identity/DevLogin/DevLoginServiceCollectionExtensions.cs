using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.Identity;

public static class DevLoginServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the account service with the developer-testing sign-in (see <see cref="DevLoginAccountService"/>). Legacy mode only: the host calls
    /// this instead of <c>AddUserIdentity</c>. The host also registers an <see cref="IRequestOrigin"/>.
    /// </summary>
    public static IServiceCollection AddDevLogin(this IServiceCollection services, bool enabled, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new DevLoginOptions(enabled, isDevelopment));
        services.Replace(ServiceDescriptor.Scoped<IUserAccountService, DevLoginAccountService>());
        return services;
    }
}
