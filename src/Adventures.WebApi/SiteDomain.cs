using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Adventures.WebApi;

/// <summary>
/// The installation's domain as one setting (<c>Site:Domain</c>, for example <c>example.com</c>). When it is set it decides the browser origins the API
/// allows, the sign-in tenant and the addresses of the API and MCP sites (<c>api.</c> and <c>mcp.</c> under it), so an installer changes one value
/// instead of several files. When it is not set the explicit settings apply, which keeps an existing installation unchanged. A value that is not a
/// plain domain name stops the startup: it must never turn into a trusted origin.
/// </summary>
public static partial class SiteDomain
{
    public const string Key = "Site:Domain";

    /// <summary>The lower-case domain, or null when none is set. Throws <see cref="InvalidOperationException"/> for a value that is not a bare domain name with at least one dot.</summary>
    public static string? Domain(IConfiguration configuration)
    {
        var value = configuration[Key]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        value = value.ToLowerInvariant();
        return DomainName().IsMatch(value)
            ? value
            : throw new InvalidOperationException($"{Key} must be a plain domain name such as example.com (no scheme, port, path or wildcard).");
    }

    /// <summary>Origins the browser may call the API from: the domain and its www host, plus the local Angular server in development only.</summary>
    public static IReadOnlyList<string> AllowedOrigins(IConfiguration configuration, bool isDevelopment)
    {
        if (Domain(configuration) is not { } domain)
        {
            return configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        }

        var origins = new List<string> { $"https://{domain}", $"https://www.{domain}" };
        if (isDevelopment)
        {
            origins.AddRange(["http://localhost:4200", "https://localhost:4200"]);
        }

        return origins;
    }

    /// <summary>The tenant sign-in uses: the domain, otherwise <c>Auth:Tenant</c>, otherwise <paramref name="fallback"/>.</summary>
    public static string Tenant(IConfiguration configuration, string fallback) => Domain(configuration) ?? configuration["Auth:Tenant"] ?? fallback;

    /// <summary>The MCP site address (<c>McpServer:BaseUrl</c> unless the domain is set); null when neither is set.</summary>
    public static string? McpBaseUrl(IConfiguration configuration) => Domain(configuration) is { } domain ? $"https://mcp.{domain}/" : configuration["McpServer:BaseUrl"];

    /// <summary>The API site address (<c>Api:BaseUrl</c> unless the domain is set), or <paramref name="fallback"/> when neither is set.</summary>
    public static string? ApiBaseUrl(IConfiguration configuration, string? fallback = null) =>
        Domain(configuration) is { } domain ? $"https://api.{domain}/" : configuration["Api:BaseUrl"] ?? fallback;

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainName();
}
