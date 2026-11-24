// Startup composition: CORS, controllers, authentication, M2M, HTTP clients, failure reporting, AI triage (moved out of Program.cs, no behaviour change).
using Adventures.Common.Diagnostics;
using Adventures.Data;
using Adventures.Data.Postgres;
using Adventures.Identity.Postgres;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.Identity;
using Adventures.Ioc.Extensions;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Adventures.Blog;
using Adventures.Blog.Legacy;
using Adventures.Blog.WebApi;
using Adventures.Collaboration.WebApi;
using Adventures.Diagnostics.WebApi;
using Adventures.Identity.WebApi;
using Adventures.Tools.WebApi;
using Adventures.Blog.Postgres;
using Adventures.Collaboration;
using Adventures.MEF;

namespace Adventures.Hosting;

internal static class AppPlatformServices
{
    public const string AngularCorsPolicy = "Angular";

    public static IServiceCollection AddAppPlatform(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
            var allowedOrigins = Adventures.WebApi.SiteDomain.AllowedOrigins(configuration, isDevelopment).ToArray();
            // The sites that may call the API: the fixed origins above plus every domain, www host and alias registered on a tenant (read from the data, kept 60 s),
            // so a site's temporary address works as soon as it is registered, with no wildcard and no redeploy.
            services.AddTenantCors(AngularCorsPolicy, allowedOrigins);

            services.AddControllers()
                .AddBlogControllers()
                .AddCollaborationControllers()
                .AddDiagnosticsControllers()
                .AddIdentityControllers()
                .AddToolsControllers()
                .AddApplicationPart(typeof(RuntimeController).Assembly);
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            services.AddOpenApi();

            services.AddSharedJwtAuthentication(configuration);
            services.AddRequestHardening(configuration);
            services.AddSingleton(_ => new HealthSnapshotCache(HealthSnapshotCache.WindowFrom(configuration), TimeProvider.System));

            // Each machine client's secret is named for its purpose (ClientSecret__ClaudeAuthor); the older position-based M2MClients__Clients__n__Secret still works.
            services.AddM2MClients(configuration);
            services.AddSingleton<IClientSecretHasher, ClientSecretHasher>();
            services.AddSingleton<IClientCredentialStore, InMemoryClientCredentialStore>();
            // The two scopes of the first hello-world (mcp.postgres.query, mcp.filesearch.search) guarded nothing and are no longer registered as policies (R4).
            services.AddScopeAuthorization(McpDiagnosticsController.RequiredScope, Adventures.Blog.WebApi.AuthoringController.RequiredScope);
            services.AddRoleAuthorization(WellKnownClaims.TenantAdminRole);

            // McpServer:BaseUrl -> mcp.global-webnet.com. HealthController calls this over M2M auth (minting
            // its own "mcp-host" token in-process via IJwtTokenService, since this app IS the token issuer -
            // see docs/artifacts/2026-09-23-mcp-m2m-hello-world.md) to prove the cross-site M2M pipeline end
            // to end. A short timeout keeps a slow/unreachable mcp site from hanging /api/health itself.
            // No retries: this client only serves the health probe, so /api/health answers promptly and says what failed (the default resilience handler would turn one failure into eight seconds of waiting).
            services.AddServiceClient("McpServer", sp => new Uri(Adventures.WebApi.SiteDomain.McpBaseUrl(sp.GetRequiredService<IConfiguration>())
                ?? throw new InvalidOperationException("McpServer:BaseUrl is not configured.")), TimeSpan.FromSeconds(10), noRetries: true);
            services.AddCorrelationId();
            services.AddFailureReporting();
            services.AddScoped<IDependencyProbes>(sp => new DependencyProbes(
                sp.GetRequiredService<IJwtTokenService>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetService<Adventures.Common.Diagnostics.IStoreProbe>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<DependencyProbes>()));

            // The AI triage (an administrator's button): Anthropic:ApiKey is a secret (user-secrets locally, the host's environment
            // secret Anthropic__ApiKey in production); without it the button answers "not set up". One call can take many seconds
            // and is never retried automatically (a retry would double the cost), so this client opts out of the default resilience.
            services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        #pragma warning disable EXTEXP0001
            services.AddHttpClient(AiTriage.ClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.anthropic.com/");
                client.Timeout = TimeSpan.FromSeconds(90);
            }).RemoveAllResilienceHandlers();
        #pragma warning restore EXTEXP0001
            services.AddSingleton<IAiTriage, AiTriage>();
        return services;
    }
}
