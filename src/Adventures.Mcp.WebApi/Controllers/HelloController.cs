// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Mcp.WebApi;

public sealed record HelloResponse(string Message);

/// <summary>
/// Minimal end-to-end proof that a machine-to-machine JWT issued by api.global-webnet.com
/// (via its /api/auth/m2m/token client-credentials endpoint) is accepted here, on a separate
/// site/app-pool, using the shared Adventures.Security JWT validation and a shared signing key.
/// See docs/artifacts/2026-09-23-mcp-m2m-hello-world.md for the full pipeline this proves out.
/// </summary>
[ApiController]
[Route("api/hello")]
public sealed class HelloController : ControllerBase
{
    public const string RequiredScope = Adventures.Security.Scopes.Hello;
    private const string RequiredPolicy = "Scope:" + RequiredScope;

    [HttpGet]
    [Authorize(Policy = RequiredPolicy)]
    public ActionResult<HelloResponse> Get() => Ok(new HelloResponse("hello world"));
}
