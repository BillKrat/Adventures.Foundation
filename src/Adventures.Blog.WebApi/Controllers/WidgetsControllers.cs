using Adventures.Blog;
using Adventures.Entities;
using Adventures.MEF;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

/// <summary>What the Widgets screen shows: the widgets that can be placed, where each placement sits, and the four zones.</summary>
public sealed record WidgetsOverview(IReadOnlyList<WidgetInfo> Available, IReadOnlyList<WidgetPlacementModel> Placements, IReadOnlyList<string> Zones);

public sealed record NewPlacement(string Zone, string WidgetKey, string? Title, Dictionary<string, string>? Settings);

public sealed record PlacementChange(string? Title, Dictionary<string, string>? Settings);

public sealed record MovePlacement(int Direction);

/// <summary>
/// The Widgets screen for administrators: which widgets exist (those of the plug-ins that are on), where they sit in the four zones, and adding, changing,
/// moving and removing a placement. Zone layout is a site setting, so it needs the site-settings right.
/// </summary>
[ApiController]
[Route("api/widgets")]
[RequireRight(Rights.SettingsManage)]
public sealed class WidgetsController(IWidgetPlacements placements, IWidgetRenderer renderer) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WidgetsOverview>> Overview(CancellationToken cancellationToken) =>
        Ok(new WidgetsOverview(
            await renderer.AvailableAsync(cancellationToken),
            await placements.ListAsync(cancellationToken),
            WidgetZones.All));

    [HttpPost("placements")]
    public Task<IActionResult> Place([FromBody] NewPlacement request, CancellationToken cancellationToken) =>
        GuardedAsync(async () => Ok(await placements.AddAsync(request.Zone, request.WidgetKey, request.Title, request.Settings, cancellationToken)));

    [HttpPut("placements/{id}")]
    public Task<IActionResult> Change(string id, [FromBody] PlacementChange request, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await placements.UpdateAsync(id, request.Title, request.Settings, cancellationToken) is { } changed ? Ok(changed) : NotFound());

    [HttpPost("placements/{id}/move")]
    public Task<IActionResult> Move(string id, [FromBody] MovePlacement request, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await placements.MoveAsync(id, request.Direction, cancellationToken) ? NoContent() : NotFound());

    [HttpDelete("placements/{id}")]
    public Task<IActionResult> Remove(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await placements.RemoveAsync(id, cancellationToken) ? NoContent() : NotFound());
}

/// <summary>A zone as a visitor's browser reads it: the placed widgets' titles and sanitised HTML, in order. Anonymous and read-only; nothing but rendered output leaves.</summary>
[ApiController]
[Route("api/public/widgets")]
[AllowAnonymous]
public sealed class PublicWidgetsController(IWidgetPlacements placements, IRightsAuthorizer authorizer) : ControllerBase
{
    /// <summary>A widget that names a required right (the Administration widget) is left out unless the caller holds it; everyone else sees the rest.</summary>
    [HttpGet("{zone}")]
    public async Task<ActionResult<IReadOnlyList<RenderedWidget>>> Zone(string zone, CancellationToken cancellationToken)
    {
        if (!WidgetZones.IsValid(zone))
        {
            return NotFound();
        }

        var shown = new List<RenderedWidget>();
        foreach (var widget in await placements.RenderZoneAsync(zone, cancellationToken))
        {
            if (widget.RequiredRight is null || await authorizer.IsAllowedAsync(User, widget.RequiredRight, cancellationToken))
            {
                shown.Add(widget);
            }
        }

        return Ok(shown);
    }
}
