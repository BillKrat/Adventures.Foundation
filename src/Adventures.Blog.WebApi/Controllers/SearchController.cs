using Adventures.Blog;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Adventures.Blog.WebApi;

/// <summary>
/// Search of the blog's own published posts and pages, for visitors (anonymous, read-only, rate limited per client address): words only come back, never a
/// draft, a revision or markup. A query that is too short or too long is a plain refusal.
/// </summary>
[ApiController]
[Route("api/public/search")]
[AllowAnonymous]
public sealed class SearchController(IContentSearcher searcher) : ApiControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RequestHardening.SearchPolicy)]
    public Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = ContentSearch.DefaultPageSize, CancellationToken cancellationToken = default) =>
        GuardedAsync(async () => Ok(await searcher.SearchAsync(q, page, pageSize, cancellationToken)));
}
