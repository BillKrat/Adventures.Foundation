using System.Text.RegularExpressions;

namespace Adventures.Browser.Client;

/// <summary>A domain as a person types it, made into the name the browser API knows a site by.</summary>
public static partial class DomainName
{
    /// <summary>
    /// The host name in lower case; a pasted web address gives its host. Null for anything else, including an address that carries a user name, so nothing
    /// secret is ever kept as a "domain".
    /// </summary>
    public static string? Normalize(string? typed)
    {
        var text = (typed ?? string.Empty).Trim().TrimEnd('/');
        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var address) || address.UserInfo.Length > 0
                || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
            {
                return null;
            }

            text = address.Host;
        }

        return HostName().IsMatch(text) ? text.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$")]
    private static partial Regex HostName();
}
