using Adventures.Blog.Legacy.Research;
using Adventures.Mcp.WebApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Legacy.Tests;

/// <summary>R5 of the host reuse review: the legacy research tools are an optional module a host adds with one call.</summary>
public sealed class ResearchModuleTests
{
    [Fact]
    public void One_call_adds_the_research_tools_and_their_scope_to_the_gateway()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMcpGateway(new McpGatewayOptions());
        services.AddBlogResearchTools();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<McpOnlyToolRegistry>();

        Assert.NotEmpty(registry.Tools);
        Assert.All(registry.Tools, tool => Assert.Equal(ResearchTools.Scope, tool.RequiredScope));
        Assert.NotNull(registry.Find("blogai_rows"));
    }

    [Fact]
    public void Without_the_module_the_gateway_holds_no_research_tool()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpGateway(new McpGatewayOptions());
        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetRequiredService<McpOnlyToolRegistry>().Tools);
    }
}
