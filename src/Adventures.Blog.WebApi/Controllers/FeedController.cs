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
    /// <summary>How many of the newest posts the feed lists.</summary>
    public const int MaxItems = 20;

    [HttpGet]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var domain = tenant.Current.Domain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            return NotFound();
        }

        var posts = await content.ListPostsAsync(1, MaxItems, category: null, tag: null, cancellationToken, includeDrafts: false);
        var site = await settings.GetPublicAsync(cancellationToken);
        return Content(RssFeed.Build(site, $"https://{domain}", posts.Posts), RssFeed.ContentType);
    }
}
