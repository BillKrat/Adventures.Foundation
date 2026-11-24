using Adventures.Mcp.WebApi;
using Adventures.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Blog.Legacy.Research;

/// <summary>The research tools as a source the MCP gateway collects (one reader, every catalogue tool and the paged rows tool).</summary>
public sealed class BlogResearchToolSource(IBlogAiReader reader) : IMcpOnlyToolSource
{
    public IEnumerable<IMcpOnlyTool> Tools => ResearchTools.Create(reader);
}

public static class BlogResearchModule
{
    /// <summary>
    /// Adds the legacy BlogEngine.NET research tools to an MCP host built on the gateway (optional, R5 of the host reuse review): the SELECT-only reader
    /// (<c>ConnectionStrings:BlogAi</c>), the tools and the <c>blogai.read</c> scope policy. A host that does not call this has no SQL Server code on its path.
    /// </summary>
    public static IServiceCollection AddBlogResearchTools(this IServiceCollection services)
    {
        services.AddSingleton<IBlogAiReader, BlogAiReader>();
        services.AddScopeAuthorization(ResearchTools.Scope);
        services.AddSingleton<IMcpOnlyToolSource, BlogResearchToolSource>();
        return services;
    }
}
