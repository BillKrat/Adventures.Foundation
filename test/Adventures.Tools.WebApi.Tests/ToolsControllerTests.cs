using System.Security.Claims;
using Adventures.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Tools.WebApi.Tests;

/// <summary>GET /api/tools: the manifest of what this token and the enabled plug-ins allow, and a find that goes through the replaceable selector.</summary>
public class ToolsControllerTests
{
    private sealed class FakePlugins(params string[] enabled) : Adventures.MEF.IPlugins
    {
        public Task<IReadOnlyList<Adventures.MEF.PluginState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(enabled.Contains(key));

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(enabled));

        public Task SetEnabledAsync(string key, bool enabledNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static AppTool Tool(string name, string scope, params string[] plugins) =>
        new(name, $"{name} does its work", """{ "type": "object", "properties": { "id": { "type": "string" } } }""", scope, "GET", $"api/{name}", plugins);

    private static ToolsController Build(string? scopeClaim, params string[] enabledPlugins)
    {
        var catalog = new ToolCatalog(
        [
            Tool("list_my_posts", "content.draft"),
            Tool("list_my_collaboration", "content.draft", "developer-collaboration"),
            Tool("recent_exceptions", "mcp.diagnostics.read"),
        ]);
        var claims = scopeClaim is null ? [] : new[] { new Claim("scope", scopeClaim) };
        return new ToolsController(catalog, new ToolSelector(), new FakePlugins(enabledPlugins))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } },
        };
    }

    private static IReadOnlyList<ToolDescription> Listed(IActionResult result) =>
        Assert.IsAssignableFrom<IReadOnlyList<ToolDescription>>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task TheListHoldsOnlyWhatTheTokenScopesAllow()
    {
        var tools = Listed(await Build("content.draft", "developer-collaboration").List(CancellationToken.None));

        Assert.Equal(["list_my_posts", "list_my_collaboration"], tools.Select(t => t.Name));
    }

    [Fact]
    public async Task AScopeValueIsSpaceDelimitedLikeTheScopePolicyReadsIt()
    {
        var tools = Listed(await Build("content.draft mcp.diagnostics.read", "developer-collaboration").List(CancellationToken.None));

        Assert.Equal(3, tools.Count);
    }

    [Fact]
    public async Task ATokenWithoutScopesGetsAnEmptyList()
    {
        Assert.Empty(Listed(await Build(null, "developer-collaboration").List(CancellationToken.None)));
    }

    [Fact]
    public async Task ADisabledPluginsToolIsNotListed()
    {
        var tools = Listed(await Build("content.draft").List(CancellationToken.None));

        Assert.Equal(["list_my_posts"], tools.Select(t => t.Name));
    }

    [Fact]
    public async Task TheDescriptionCarriesTheInputSchemaAsRealJson()
    {
        var tool = Listed(await Build("content.draft").List(CancellationToken.None)).Single();

        Assert.Equal(System.Text.Json.JsonValueKind.Object, tool.InputSchema.ValueKind);
        Assert.Equal("GET", tool.Method);
        Assert.Equal("api/list_my_posts", tool.Path);
        Assert.Equal("content.draft", tool.RequiredScope);
    }

    [Fact]
    public async Task FindReturnsTheToolThatHandlesTheKey()
    {
        var found = Assert.IsType<OkObjectResult>(await Build("content.draft", "developer-collaboration").Find("collaboration", CancellationToken.None));

        var answer = Assert.IsType<FoundTool>(found.Value);
        Assert.False(answer.Fallback);
        Assert.Equal("list_my_collaboration", answer.Tool!.Name);
    }

    [Fact]
    public async Task FindNeverOffersAToolOutsideTheCallersScopes()
    {
        var found = Assert.IsType<OkObjectResult>(await Build("content.draft").Find("exceptions", CancellationToken.None));

        var answer = Assert.IsType<FoundTool>(found.Value);
        Assert.True(answer.Fallback);
        Assert.Null(answer.Tool);
    }

    [Fact]
    public async Task FindWithNothingMatchingSaysItIsTheFallback()
    {
        var answer = Assert.IsType<FoundTool>(Assert.IsType<OkObjectResult>(await Build("content.draft").Find("zzzz qqqq", CancellationToken.None)).Value);

        Assert.True(answer.Fallback);
    }

    [Fact]
    public async Task FindWithoutAKeyIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(await Build("content.draft").Find("  ", CancellationToken.None));
    }
}
