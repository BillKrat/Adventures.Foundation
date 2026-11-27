using Adventures.WebApi;

namespace Adventures.Browser.Host;

public static class BrowserSecurityHeaders
{
    /// <summary>
    /// The policy of a host that serves one page and a JSON API: scripts and styles only from this host (no inline code, no eval, no outside
    /// origin), requests only back to this host, no framing. The API-only policy of the shared library (<c>default-src 'none'</c>) would
    /// block the page itself.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>The shared security headers of <c>Adventures.WebApi</c> with this host's own policy (R6 of the host reuse review: the header set used to be a second copy here).</summary>
    public static IApplicationBuilder UseBrowserSecurityHeaders(this IApplicationBuilder app, bool isDevelopment) =>
        app.UseSecurityHeaders(isDevelopment, ContentSecurityPolicy);
}
