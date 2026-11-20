using System.Text.Json;
using Adventures.MEF;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Tools.WebApi;

/// <summary>One tool as a client reads it: what it is called, what it takes, and the API call it stands for.</summary>
public sealed record ToolDescription(string Name, string Description, JsonElement InputSchema, string RequiredScope, string Method, string Path);

/// <summary>The answer of a search: the tool the selector chose, or <c>Fallback</c> when no tool handled the key.</summary>
public sealed record FoundTool(ToolDescription? Tool, bool Fallback);

/// <summary>
/// The tool manifest. A client without MCP asks here which tools it may use; the MCP server reads the same list. A caller sees a tool only
/// when its token carries the tool's scope and the tool's plug-in (when it has one) is on, so this endpoint never advertises what the API
/// would refuse. Any signed-in caller may ask; it reveals nothing the caller could not already call.
/// </summary>
[ApiController]
[Route("api/tools")]
[Authorize]
public sealed class ToolsController(ToolCatalog catalog, IToolSelector selector, IPlugins plugins) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(Describe(await AvailableAsync(cancellationToken)));

    /// <summary>Finds the tool for a search key among the tools this caller may use.</summary>
    [HttpGet("find")]
    public async Task<IActionResult> Find([FromQuery] string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest("A search key is required.");
        }

        var chosen = selector.Select(key, await AvailableAsync(cancellationToken));
        return Ok(chosen is DefaultFallbackTool ? new FoundTool(null, true) : new FoundTool(Describe(chosen), false));
    }

    private async Task<IReadOnlyList<IAppTool>> AvailableAsync(CancellationToken cancellationToken)
    {
        var scopes = User.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        return catalog.Available(scopes, await plugins.EnabledKeysAsync(cancellationToken));
    }

    private static IReadOnlyList<ToolDescription> Describe(IEnumerable<IAppTool> tools) => tools.Select(Describe).ToList();

    private static ToolDescription Describe(IAppTool tool) =>
        new(tool.Name, tool.Description, JsonDocument.Parse(tool.InputSchema).RootElement.Clone(), tool.RequiredScope, tool.Method, tool.PathTemplate);
}
