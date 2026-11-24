using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
// Assessed 2026-10-24 17:51 CT (Claude): automated scan for secrets, injection and unsafe patterns, re-run after the fixes - security findings F-1 to F-3 fixed in this file or in code it uses, covered by tests. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Mcp.WebApi;

public sealed record McpHealthResponse(string Status, DateTimeOffset TimestampUtc);

[Microsoft.AspNetCore.Authorization.AllowAnonymous]
[ApiController]
[Route("api/health")]
public sealed class McpHealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<McpHealthResponse> Get() => Ok(new McpHealthResponse("Healthy", DateTimeOffset.UtcNow));
}
