// Startup composition: the user vault and credentials, or the Legacy developer sign-in (moved out of Program.cs, no behaviour change).
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
using Adventures.Blog.Postgres;
using Adventures.Collaboration;
using Adventures.MEF;

namespace Adventures.Hosting;

internal static class AppIdentityServices
{
    public static IServiceCollection AddAppIdentity(this IServiceCollection services, bool legacy, IConfiguration configuration, bool isDevelopment)
    {
            if (legacy)
            {
                // No vault and no user store in Legacy: the developer-testing sign-in only, behind four gates (Legacy mode, Legacy:DevLogin=true in user-secrets,
                // the Development environment, a request from this machine). A deployed site cannot satisfy them.
                services.AddHttpContextAccessor();
                services.AddScoped<IRequestOrigin, HttpRequestOrigin>();
                services.AddScoped<IUserProvisioning, LegacyUserProvisioning>();
                services.AddDevLogin(
                    enabled: string.Equals(configuration[DevLoginOptions.SettingName], "true", StringComparison.OrdinalIgnoreCase),
                    isDevelopment: isDevelopment);
            }
            else
            {
                services.AddSingleton<ISqlExecutor>(_ =>
                new NpgsqlSqlExecutor(configuration.GetConnectionString("Postgres")
                    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.")));
            services.AddScoped<IEntityRecordRepository, PostgresEntityRepository>();
            services.AddUserIdentity();
            services.AddPostgresUserCredentials();
            // Start-up task: make sure the sign-in tables exist (runs before the identity upgrades registered with the entities).
            services.AddIdentitySchemaStartupTask();
            // A fresh install makes its own sign-in tables, and the first host on an empty install becomes the first site with its own Admin (onboarding stage 2).
            services.AddSingleton(provider => new IdentitySchemaInitializer(provider.GetRequiredService<ISqlExecutor>()));
            services.AddScoped<IFirstPlatformAdministrator>(provider => provider.GetRequiredService<PlatformAdministratorBll>());
            services.AddScoped<IFirstRunSite, FirstRunSite>();
            services.AddScoped<ISiteProvisioner, SiteProvisioner>();   // the setup page can start a new site (onboarding stage 3)
            }
        return services;
    }
}
