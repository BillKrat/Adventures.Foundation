// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Blog;

namespace Adventures.Blog.WebApi;

/// <summary>The site settings for administrators (the six standard admin actions): SiteTitle, Tagline, PostsPerPage, TimeZone.</summary>
[ApiController]
[Route("api/settings")]
[AdminRights(Read = Rights.SettingsManage, Create = Rights.SettingsManage, Update = Rights.SettingsManage, Delete = Rights.SettingsManage)]
public sealed class SettingsController(IEntityPresenter<SiteSetting> presenter) : AdminEntityControllerBase<SiteSetting>(presenter);

/// <summary>The part of the settings a visitor's browser needs (the site title, the tagline, how many posts a page shows): anonymous, read-only, no secrets.</summary>
[ApiController]
[Route("api/public/site")]
[AllowAnonymous]
public sealed class SiteController(ISiteSettings settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicSiteSettings>> Get(CancellationToken cancellationToken) => Ok(await settings.GetPublicAsync(cancellationToken));
}
