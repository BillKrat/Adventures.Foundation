using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Mcp.WebApi;

/// <summary>What differs between MCP servers built on the gateway: the name it announces, the text a client reads first, and the scopes of its own research tools.</summary>
public sealed class McpGatewayOptions
{
    public string ServerName { get; set; } = "adventures-mcp";

    public string ServerVersion { get; set; } = "1.1.0";

    public string Instructions { get; set; } = "Diagnostics (read-only): start with recent_exceptions or exception_signatures, then diagnose_request with a correlation id. Authoring (drafts only): create_draft_post and update_draft_post write Markdown drafts that an administrator publishes.";

    /// <summary>Scopes beyond the shared <see cref="Scopes.ContentDraft"/> and <see cref="Scopes.DiagnosticsRead"/> and the scopes of the registered <see cref="IMcpOnlyTool"/> tools (those are found from the registry).</summary>
    public IReadOnlyList<string> AdditionalScopes { get; set; } = [];

    /// <summary>Where the API is when <c>Api:BaseUrl</c> is not set.</summary>
    public string DefaultApiBaseUrl { get; set; } = "https://api.global-webnet.com/";
}

public static class McpGatewayExtensions
{
    /// <summary>Registers the gateway services: the options, scope policies, the API client (named <c>Api</c>, 15 s, carrying the correlation id), the manifest cache and the tool registry built from every registered <see cref="IMcpOnlyTool"/>.</summary>
    public static IServiceCollection AddMcpGateway(this IServiceCollection services, McpGatewayOptions options)
    {
        services.AddSingleton(options);
        services.AddScopeAuthorization([Scopes.Hello, Scopes.DiagnosticsRead, Scopes.ContentDraft, .. options.AdditionalScopes]);
        services.AddServiceClient(ApiForwardingClient.ApiClientName, sp => new Uri(SiteDomain.ApiBaseUrl(sp.GetRequiredService<IConfiguration>(), options.DefaultApiBaseUrl)!), TimeSpan.FromSeconds(15));
        services.AddMemoryCache();
        services.AddCorrelationId();
        services.AddScoped<ApiForwardingClient>();
        services.AddSingleton(sp => new McpOnlyToolRegistry(sp.GetServices<IMcpOnlyTool>().Concat(sp.GetServices<IMcpOnlyToolSource>().SelectMany(source => source.Tools))));
        return services;
    }

    /// <summary>Makes MVC find the gateway controllers (<c>POST /mcp</c>, <c>/api/hello</c>, <c>/api/health</c>). Call on the builder returned by <c>AddControllers()</c>.</summary>
    public static IMvcBuilder AddMcpControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddApplicationPart(typeof(McpGatewayExtensions).Assembly);
    }
}
