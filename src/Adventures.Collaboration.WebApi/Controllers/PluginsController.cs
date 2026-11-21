// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
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

/// <summary>The Plugins page for administrators: the plug-ins with their state (built-in and discovered), the discovery report, and switching one on or off (the admin menu follows).</summary>
[ApiController]
[Route("api/plugins")]
[RequireRight(Rights.PluginsManage)]
public sealed class PluginsController(IPlugins plugins, IEnumerable<PluginLoadReport> reports, IPluginSettings settings) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PluginState>>> List(CancellationToken cancellationToken) =>
        Ok(await plugins.ListAsync(cancellationToken));

    /// <summary>What plug-in discovery loaded at start-up and what it refused (an unapproved or broken DLL), so a refusal is visible and never silent.</summary>
    [HttpGet("load-report")]
    public ActionResult<PluginLoadReport> LoadReport() =>
        Ok(reports.FirstOrDefault() ?? new PluginLoadReport([], []));

    /// <summary>The Customize screen of one plug-in: the settings it declares and their current values (404 for an unknown plug-in).</summary>
    [HttpGet("{key}/settings")]
    public async Task<ActionResult<PluginSettingsModel>> GetSettings(string key, CancellationToken cancellationToken)
    {
        try
        {
            var view = await settings.GetForDisplayAsync(key, cancellationToken);
            return Ok(new PluginSettingsModel(settings.DefinitionsOf(key), view.Values, view.SecretsSet));
        }
        catch (BusinessRuleException)
        {
            return NotFound();
        }
    }

    /// <summary>Saves the given values of one plug-in; a value its rules refuse saves nothing and comes back as the reason (409).</summary>
    [HttpPut("{key}/settings")]
    public async Task<IActionResult> UpdateSettings(string key, [FromBody] PluginSettingsUpdate update, CancellationToken cancellationToken)
    {
        try
        {
            _ = settings.DefinitionsOf(key);
        }
        catch (BusinessRuleException)
        {
            return NotFound();
        }

        return await GuardedAsync(async () =>
        {
            await settings.SetAsync(key, update.Values, update.Clear, cancellationToken);
            return NoContent();
        });
    }

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

/// <summary>A plug-in's settings for its Customize screen: what it declares, and the value of each now (the default where none is stored).</summary>
public sealed record PluginSettingsModel(IReadOnlyList<PluginSettingDefinition> Definitions, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> SecretsSet);

/// <summary>The values an administrator saves; only settings the plug-in declares are accepted, and a bad value saves nothing. An empty secret keeps what is stored; a secret is removed only by naming it in <see cref="Clear"/>.</summary>
public sealed record PluginSettingsUpdate(Dictionary<string, string?> Values, string[]? Clear = null);
