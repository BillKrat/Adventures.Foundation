using System.Text.Json;
using System.Text.Json.Nodes;
using Adventures.Mcp.WebApi;
using Adventures.Security;
using Xunit;

namespace Adventures.Mcp.WebApi.Tests;

/// <summary>R4 of the host reuse review: the MCP gateway is a library; its tool registry and the shared scope names are pinned here so a private host reuses them unchanged.</summary>
public sealed class GatewayContractTests
{
    private sealed class FakeTool(string name, string scope) : IMcpOnlyTool
    {
        public string Name => name;
        public string Description => "d";
        public string RequiredScope => scope;
        public JsonObject InputSchema => new() { ["type"] = "object" };
        public Task<McpToolOutcome> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken) => Task.FromResult(new McpToolOutcome("ok", false));
    }

    [Fact]
    public void The_shared_scope_names_do_not_change()
    {
        Assert.Equal("content.draft", Scopes.ContentDraft);
        Assert.Equal("mcp.diagnostics.read", Scopes.DiagnosticsRead);
        Assert.Equal("mcp.hello", Scopes.Hello);
    }

    [Fact]
    public void A_registry_lists_nothing_without_the_scope_and_everything_with_it()
    {
        var registry = new McpOnlyToolRegistry([new FakeTool("a", "x.read"), new FakeTool("b", "x.read")]);
        Assert.Empty(registry.VisibleTo(new HashSet<string>()));
        Assert.Equal(2, registry.VisibleTo(new HashSet<string> { "x.read" }).Count);
        Assert.NotNull(registry.Find("a"));
        Assert.Null(registry.Find("zzz"));
    }

    [Fact]
    public void A_registry_refuses_a_duplicate_name_and_a_tool_with_no_scope()
    {
        Assert.Throws<ArgumentException>(() => new McpOnlyToolRegistry([new FakeTool("a", "x"), new FakeTool("a", "x")]));
        Assert.Throws<ArgumentException>(() => new McpOnlyToolRegistry([new FakeTool("a", " ")]));
    }

    [Fact]
    public void The_gateway_options_default_to_no_extra_scopes()
    {
        var options = new McpGatewayOptions();
        Assert.Empty(options.AdditionalScopes);
        Assert.False(string.IsNullOrWhiteSpace(options.ServerName));
    }
}
