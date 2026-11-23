using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

/// <summary>
/// Running behind a CDN or reverse proxy. The app then sees the proxy's address, not the visitor's, so every per-address limit would be shared by all visitors, and the https redirect would loop
/// when the proxy speaks plain http to the app. <c>Proxy:Trusted</c> lists the proxy addresses (an address, or a range such as <c>198.51.100.0/24</c>; a list in the settings file, or one
/// comma-separated value such as an environment variable gives). Only a request whose immediate caller is on that list may tell us the visitor's address and the scheme
/// (<c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c>); a header from anyone else is ignored. The forwarded HOST is never used: the tenant is decided from the host the server saw. With nothing
/// listed (the default) nothing is forwarded at all. <c>Proxy:ForwardLimit</c> (default 1) is how many proxies in a row are believed, counted from the app.
/// </summary>
public static class TrustedProxies
{
    public const string Key = "Proxy:Trusted";
    public const string LimitKey = "Proxy:ForwardLimit";

    /// <summary>The listed proxy entries, trimmed, empty ones dropped.</summary>
    public static IReadOnlyList<string> Entries(IConfiguration configuration)
    {
        var section = configuration.GetSection(Key);
        return new[] { section.Value }
            .Concat(section.GetChildren().Select(child => child.Value))
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();
    }

    public static bool IsEnabled(IConfiguration configuration) => Entries(configuration).Count > 0;

    /// <summary>Fills the middleware options from the settings. A bad entry stops the start naming the setting: ignoring it would silently trust nothing, widening it would trust everyone.</summary>
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = int.TryParse(configuration[LimitKey], out var limit) && limit > 0 ? limit : 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var entry in Entries(configuration))
        {
            if (entry.Contains('/'))
            {
                if (!System.Net.IPNetwork.TryParse(entry, out var network))
                {
                    throw new InvalidOperationException($"{Key} has an entry that is not a valid address range: '{entry}'. Use an address such as 10.0.0.5 or a range such as 198.51.100.0/24.");
                }

                options.KnownIPNetworks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                throw new InvalidOperationException($"{Key} has an entry that is not a valid address: '{entry}'. Use an address such as 10.0.0.5 or a range such as 198.51.100.0/24.");
            }
        }

        // the middleware trusts EVERY caller when both lists are empty: with nothing listed, list an address no caller can have (255.255.255.255), so nothing is ever trusted
        if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
        {
            options.KnownProxies.Add(IPAddress.None);
        }
    }

    /// <summary>Registers the forwarded-header options when a proxy is listed (a no-op otherwise).</summary>
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        if (IsEnabled(configuration))
        {
            services.Configure<ForwardedHeadersOptions>(options => Configure(options, configuration));
        }

        return services;
    }

    /// <summary>Adds the forwarded-header middleware when a proxy is listed. Put it first, before logging, the https redirect and the rate limiter.</summary>
    public static IApplicationBuilder UseTrustedProxies(this IApplicationBuilder app, IConfiguration configuration) =>
        IsEnabled(configuration) ? app.UseForwardedHeaders() : app;
}
