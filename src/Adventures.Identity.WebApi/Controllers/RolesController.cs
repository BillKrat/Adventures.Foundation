// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

/// <summary>
/// The Roles list for administrators, on the grid contract, and the rights checklist of a role. The list is read-only (the presenter offers no create, update or
/// delete); the roles themselves are the ones already in the baseline data.
/// </summary>
[ApiController]
[Route("api/roles")]
[RequireRight(Rights.RolesManage)]
public sealed class RolesController(IEntityPresenter<Role> presenter, IRoleRightsEditor rightsEditor) : EntityControllerBase<Role>
{
    [HttpGet]
    public Task<ActionResult<EntityGridModel>> List([FromQuery] EntityGridQuery query, CancellationToken cancellationToken) =>
        GridAsync(presenter, query, cancellationToken);

    [HttpGet("{id}")]
    public Task<ActionResult<EntityFormModel>> Get(string id, CancellationToken cancellationToken) =>
        GetAsync(presenter, id, cancellationToken);

    /// <summary>Every right with its description: the checklist the role editor shows.</summary>
    [HttpGet("rights-catalog")]
    public IActionResult RightsCatalog() => Ok(Rights.Catalog.Where(right => right.Name != Rights.PlatformManage));

    /// <summary>
    /// Replaces what a role allows. A role is defined once for the whole platform, so changing it needs the platform right, not a tenant one (one tenant's administrator must not
    /// change what another tenant's people may do). The acting person is the signed-in one, so the editor can refuse a change that
    /// would take away their own ability to manage users and roles (409) or leave nobody with it (409).
    /// </summary>
    [HttpPut("{id}/rights")]
    [RequireRight(Rights.PlatformManage)]
    public Task<IActionResult> SetRights(string id, [FromBody] RoleRightsRequest request, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var stored = await rightsEditor.SetRightsAsync(id, request.Rights, User.Identity?.Name ?? string.Empty, cancellationToken);
            return stored is null
                ? NotFound()
                : Ok(new RoleRightsResponse(stored.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        });
}

/// <summary>The rights to give a role (each by its catalog name).</summary>
public sealed record RoleRightsRequest(IReadOnlyList<string> Rights);

/// <summary>The rights a role now holds.</summary>
public sealed record RoleRightsResponse(IReadOnlyList<string> Rights);
