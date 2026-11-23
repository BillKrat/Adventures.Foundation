using Adventures.Entities;
using Microsoft.Extensions.Hosting;

namespace Adventures.Identity.WebApi;

/// <summary>
/// The scheme and host the client was opened at, accepted only for a registered host name (or localhost in Development), with no path, query or credentials. Mail links are built on it, so
/// mail from us can never be turned into a link to a site we do not host. Plain http is accepted only for the SmarterASP temporary addresses (and localhost in Development).
/// </summary>
public static class ClientLinkBase
{
    public static async Task<string?> ResolveAsync(string? clientBase, ITenantHostSource hosts, IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(clientBase, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        var local = uri.IsLoopback && environment.IsDevelopment();
        if (!local && !(await hosts.AllNamesAsync(cancellationToken).ConfigureAwait(false)).Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!local && uri.Scheme != "https" && !uri.Host.EndsWith(".dtempurl.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".atempurl.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
