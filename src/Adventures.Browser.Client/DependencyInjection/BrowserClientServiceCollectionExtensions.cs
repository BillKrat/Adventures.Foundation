using Adventures.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Browser.Client;

public static class BrowserClientServiceCollectionExtensions
{
    /// <summary>
    /// The browser API at <paramref name="apiBaseAddress"/> (https, or this machine) and the view models. The application registers its own
    /// <see cref="ISiteListStore"/>.
    /// </summary>
    public static IServiceCollection AddAdventuresBrowserClient(this IServiceCollection services, Uri apiBaseAddress)
    {
        ApiAddress.Require(apiBaseAddress, nameof(apiBaseAddress));
        var address = apiBaseAddress.AbsoluteUri.EndsWith('/') ? apiBaseAddress : new Uri(apiBaseAddress.AbsoluteUri + "/");
        services.AddSingleton<IBrowserApi>(_ => new BrowserApi(new HttpClient(Handler()) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) }));
        return services;
    }

    // Outside a web page: a redirect is never followed (it would send a password on to another address) and no cookie is kept. In a web page the
    // browser owns both, and the setters are not supported there.
    private static HttpMessageHandler Handler()
    {
        var handler = new HttpClientHandler();
        if (!OperatingSystem.IsBrowser())
        {
            handler.AllowAutoRedirect = false;
            handler.UseCookies = false;
        }

        return handler;
    }
}
