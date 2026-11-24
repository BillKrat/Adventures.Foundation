// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.WebApi;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
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
public sealed class LookupsController(IEntityRelationProvider provider, IRightsAuthorizer authorizer) : ControllerBase
{
    /// <summary>Admin access alone is not enough (beta readiness): the choices are role, tenant and organization names, so only a person who manages or reads what the forms edit may ask.</summary>
    private static readonly string[] AnyOf =
        [Rights.UsersManage, Rights.RolesManage, Rights.PostsRead, Rights.PagesRead, Rights.TaxonomyManage, Rights.SettingsManage, Rights.PluginsManage];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RelationOption>>> Options([FromQuery] string schema, [FromQuery] string? exclude, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return BadRequest("A schema is required.");
        }

        var held = await authorizer.RightsOfAsync(User, cancellationToken);
        if (!held.Contains(Rights.All) && !AnyOf.Any(held.Contains))
        {
            return Forbid();
        }

        var options = await provider.GetOptionsAsync(schema, exclude, cancellationToken);
        return Ok(options);
    }
}
