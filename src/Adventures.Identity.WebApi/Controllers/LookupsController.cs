// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.WebApi;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

/// <summary>
/// The choices for a pointer field (roles, tenants, organizations) as id and text, for the select a form offers. The schema is
/// named by the field (<c>RefSchema</c> in the form model); only schemas the host registered answer, and only administrators ask.
/// </summary>
[ApiController]
[Route("api/lookups")]
[RequireRight(Rights.AdminAccess)]
public sealed class LookupsController(IEntityRelationProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RelationOption>>> Options([FromQuery] string schema, [FromQuery] string? exclude, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return BadRequest("A schema is required.");
        }

        var options = await provider.GetOptionsAsync(schema, exclude, cancellationToken);
        return Ok(options);
    }
}
