// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Adventures.MEF;

namespace Adventures.Collaboration.WebApi;

/// <summary>
/// Answers 404 for a route whose plug-in is switched off (any one of the named plug-ins being on is enough), so a disabled plug-in has no
/// endpoints, not only no menu entry. The stored data is untouched.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresPluginAttribute(params string[] keys) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var enabled = await context.HttpContext.RequestServices.GetRequiredService<IPlugins>().EnabledKeysAsync(context.HttpContext.RequestAborted);
        if (!keys.Any(enabled.Contains))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}

/// <summary>The Plugins page for administrators: the built-in plug-ins with their state, and switching one on or off (the admin menu follows).</summary>
[ApiController]
[Route("api/plugins")]
[RequireRight(Rights.PluginsManage)]
public sealed class PluginsController(IPlugins plugins) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PluginState>>> List(CancellationToken cancellationToken) =>
        Ok(await plugins.ListAsync(cancellationToken));

    [HttpPost("{key}/enable")]
    public Task<IActionResult> Enable(string key, CancellationToken cancellationToken) => SetAsync(key, true, cancellationToken);

    [HttpPost("{key}/disable")]
    public Task<IActionResult> Disable(string key, CancellationToken cancellationToken) => SetAsync(key, false, cancellationToken);

    private Task<IActionResult> SetAsync(string key, bool enabled, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            await plugins.SetEnabledAsync(key, enabled, cancellationToken);
            return NoContent();
        });
}
