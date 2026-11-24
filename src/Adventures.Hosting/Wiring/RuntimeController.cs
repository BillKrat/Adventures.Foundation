using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Hosting;

/// <summary>What the page needs to know about how this API is running, nothing more.</summary>
public sealed record RuntimeInfo(bool LiveData);

/// <summary>
/// One cheap anonymous fact: whether this API is a local run on the live store (the red LIVE DATA banner). It touches no dependency, so the page can
/// ask on every load without waiting for the health check, which probes the database and the MCP server and belongs on the Dashboard.
/// </summary>
[ApiController]
[Route("api/runtime")]
[AllowAnonymous]
public sealed class RuntimeController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public ActionResult<RuntimeInfo> Get() => Ok(new RuntimeInfo(configuration["LiveData:Active"] == "true"));
}
