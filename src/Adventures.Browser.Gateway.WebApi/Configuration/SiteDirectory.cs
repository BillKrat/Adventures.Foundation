using System.Text.RegularExpressions;

namespace Adventures.Browser.Gateway.WebApi;

/// <summary>
/// The configured sites, checked once at start: a bad entry stops the application instead of failing later. This is the gateway's allow-list, the
/// answer to being used to reach other addresses (server-side request forgery): a request names a domain, never an address.
/// </summary>
public sealed partial class SiteDirectory
{
    private readonly Dictionary<string, Uri> _sites = new(StringComparer.OrdinalIgnoreCase);

    public SiteDirectory(BrowserGatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        for (var i = 0; i < options.Sites.Count; i++)
        {
            var domain = (options.Sites[i].Domain ?? string.Empty).Trim();
            if (!DomainName().IsMatch(domain))
            {
                throw Invalid(i, "Domain must be a host name, without a scheme, port or path.");
            }

            if (!Uri.TryCreate((options.Sites[i].Api ?? string.Empty).Trim().TrimEnd('/') + "/", UriKind.Absolute, out var api)
                || !(api.Scheme == Uri.UriSchemeHttps || (api.Scheme == Uri.UriSchemeHttp && api.IsLoopback))
                || api.UserInfo.Length > 0 || api.Query.Length > 0 || api.Fragment.Length > 0)
            {
                throw Invalid(i, "Api must be an https address (plain http only for this machine) with no user, query or fragment: sign-ins carry passwords.");
            }

            if (!_sites.TryAdd(domain, api))
            {
                throw Invalid(i, "Domain is listed twice.");
            }
        }
    }

    /// <summary>The API address of a configured domain, or null.</summary>
    public Uri? Find(string? domain) => domain is not null && _sites.TryGetValue(domain, out var api) ? api : null;

    private static InvalidOperationException Invalid(int index, string why) => new($"{BrowserGatewayOptions.Section}:Sites:{index}: {why}");

    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$")]
    private static partial Regex DomainName();
}
