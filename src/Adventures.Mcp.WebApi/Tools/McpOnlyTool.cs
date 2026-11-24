using System.Text.Json;
using System.Text.Json.Nodes;

namespace Adventures.Mcp.WebApi;

/// <summary>What a research tool answers: the text the model sees, and whether it is an error (a refusal or an outage), so the model sees the reason.</summary>
public sealed record McpToolOutcome(string Text, bool IsError);

/// <summary>
/// A tool that exists only on the MCP server (layer 2 of the tool plan): it reads something the app's API does not offer, such as the legacy
/// BlogEngine.NET database, for deep research and not for running a blog. It is read-only by contract and by construction: a tool may only be
/// built from the legacy reader, the reader has no write method, and the MCP project references no store project (guard tests in the host keep all three). The API's own tools are not these: they come from the API's manifest.
/// </summary>
public interface IMcpOnlyTool
{
    /// <summary>The tool name an MCP client calls.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>The scope a caller token must carry to see or call this tool.</summary>
    string RequiredScope { get; }

    /// <summary>The JSON Schema of the arguments (an object).</summary>
    JsonObject InputSchema { get; }

    Task<McpToolOutcome> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>The MCP-only tools, in one list of their own, kept apart from the tools the API's manifest describes.</summary>
public sealed class McpOnlyToolRegistry
{
    private readonly IReadOnlyList<IMcpOnlyTool> _tools;

    /// <summary>Refuses a duplicate name and a tool with no scope, so a tool can never be reachable without a grant.</summary>
    public McpOnlyToolRegistry(IEnumerable<IMcpOnlyTool> tools)
    {
        _tools = tools.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in _tools)
        {
            if (string.IsNullOrWhiteSpace(tool.RequiredScope))
            {
                throw new ArgumentException($"The tool '{tool.Name}' names no scope.", nameof(tools));
            }

            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException($"The tool name '{tool.Name}' is used twice.", nameof(tools));
            }
        }
    }

    public IReadOnlyList<IMcpOnlyTool> Tools => _tools;

    public IMcpOnlyTool? Find(string name) => _tools.FirstOrDefault(t => t.Name == name);

    /// <summary>The tools a caller holding these scopes may see.</summary>
    public IReadOnlyList<IMcpOnlyTool> VisibleTo(IReadOnlySet<string> scopes) => _tools.Where(t => scopes.Contains(t.RequiredScope)).ToList();
}
