using System.Text.Json;
using System.Text.Json.Nodes;
using Adventures.Mcp.WebApi;

namespace Adventures.Blog.Legacy.Research;

/// <summary>The research tools over the legacy BlogEngine.NET database: the named queries of <see cref="BlogAiCatalog"/> and the paged table reader.</summary>
public static class ResearchTools
{
    /// <summary>Read-only access to the legacy database. Every research tool needs this scope.</summary>
    public const string Scope = "blogai.read";

    public const string RowsToolName = "blogai_rows";

    /// <summary>Every research tool, built over the one read-only reader.</summary>
    public static IEnumerable<IMcpOnlyTool> Create(IBlogAiReader reader) =>
        BlogAiCatalog.Queries.Select(query => (IMcpOnlyTool)new BlogAiQueryTool(query, reader)).Append(new BlogAiRowsTool(reader));

    internal const string NotConfigured = "The legacy database is not configured on this server (ConnectionStrings:BlogAi).";
}

/// <summary>One named, parameterised, SELECT-only legacy query as a tool. A bad argument is reported as such; anything else is logged by the exception handler and answered without detail.</summary>
internal sealed class BlogAiQueryTool(BlogAiQuery query, IBlogAiReader reader) : IMcpOnlyTool
{
    public string Name => query.Name;

    public string Description => query.Description + " Read-only.";

    public string RequiredScope => ResearchTools.Scope;

    public JsonObject InputSchema
    {
        get
        {
            var properties = new JsonObject();
            foreach (var parameter in query.Parameters)
            {
                properties[parameter.Name] = new JsonObject
                {
                    ["type"] = parameter.Kind == "int" ? "integer" : "string",
                    ["description"] = parameter.Description,
                };
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(query.Parameters.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray()),
            };
        }
    }

    public async Task<McpToolOutcome> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!reader.IsConfigured)
        {
            return new McpToolOutcome(ResearchTools.NotConfigured, true);
        }

        var supplied = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                supplied[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            }
        }

        try
        {
            return new McpToolOutcome(await reader.RunAsync(query, supplied, cancellationToken), false);
        }
        catch (BlogAiArgumentException e)
        {
            return new McpToolOutcome(e.Message, true);
        }
        catch (Microsoft.Data.SqlClient.SqlException e)
        {
            return new McpToolOutcome($"The legacy database refused the query (SQL error {e.Number}).", true);
        }
    }
}

/// <summary>One page of one allowlisted legacy table. An unknown table, or a bad number, is reported as such.</summary>
internal sealed class BlogAiRowsTool(IBlogAiReader reader) : IMcpOnlyTool
{
    public string Name => ResearchTools.RowsToolName;

    public string Description => "One page of rows of a legacy table, every column except secrets, ordered by the table's identity column. For the schema-faithful migration. Only the wave tables are readable (see the table enum). Read-only.";

    public string RequiredScope => ResearchTools.Scope;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["table"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(BlogAiTables.All.Select(t => (JsonNode)t.Name).ToArray()), ["description"] = "The legacy table." },
            ["offset"] = new JsonObject { ["type"] = "integer", ["description"] = "Rows to skip (default 0)." },
            ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = $"Rows per page (1-{BlogAiTables.MaxPageSize}, default {BlogAiTables.DefaultPageSize})." },
        },
        ["required"] = new JsonArray("table"),
    };

    public async Task<McpToolOutcome> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!reader.IsConfigured)
        {
            return new McpToolOutcome(ResearchTools.NotConfigured, true);
        }

        string? Text(string key) => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Number(string key, int fallback) => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;

        if (BlogAiTables.Find(Text("table") ?? string.Empty) is not { } table)
        {
            return new McpToolOutcome($"table must be one of: {string.Join(", ", BlogAiTables.All.Select(t => t.Name))}.", true);
        }

        try
        {
            return new McpToolOutcome(await reader.ReadRowsAsync(table, Number("offset", 0), Number("limit", BlogAiTables.DefaultPageSize), cancellationToken), false);
        }
        catch (Microsoft.Data.SqlClient.SqlException e)
        {
            return new McpToolOutcome($"The legacy database refused the query (SQL error {e.Number}); the read-only login may be missing a grant on {table.Name}.", true);
        }
    }
}
