using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

/// <summary>
/// The RSS feed of the latest published posts, anonymous and read-only. Drafts are never included (this controller never asks for them, whoever calls).
/// Links point at the tenant's own domain, taken from the request tenant, not from the API's host name; with no tenant domain there is nothing honest to link
/// to, so the feed is not found.
/// </summary>
[ApiController]
[Route("api/public/feed")]
[AllowAnonymous]
public sealed class FeedController(IPublicContent content, ISiteSettings settings, IRequestTenant tenant) : ControllerBase
{
    /// <summary>How many of the newest posts the feed lists when the site has not set the FeedItems setting.</summary>
    public const int MaxItems = SiteSettings.DefaultFeedItems;

    [HttpGet]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var domain = tenant.Current.Domain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            return NotFound();
        }

        var site = await settings.GetPublicAsync(cancellationToken);
        var posts = await content.ListPostsAsync(1, site.FeedItems, category: null, tag: null, cancellationToken, includeDrafts: false);
        return Content(RssFeed.Build(site, $"https://{SiteHost(domain)}", posts.Posts), RssFeed.ContentType);
    }

    /// <summary>A bare domain (two labels, like example.org) is linked as www, the way the public site is served (human, 2026-11-20); a host that already has a subdomain is kept.</summary>
    internal static string SiteHost(string domain)
    {
        var host = domain.Trim().TrimEnd('/');
        return host.Count(c => c == '.') == 1 ? "www." + host : host;
    }
}
