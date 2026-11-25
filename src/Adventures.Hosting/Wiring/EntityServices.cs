// Startup composition: entity repositories, tenancy, rights and the blog and collaboration features (moved out of Program.cs, no behaviour change).
using Microsoft.Extensions.DependencyInjection.Extensions;
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
using Adventures.Entities.NQuad;
using Adventures.Blog.WebApi;
using Adventures.Collaboration.WebApi;
using Adventures.Diagnostics.WebApi;
using Adventures.Identity.WebApi;
using Adventures.Blog.Postgres;
using Adventures.Collaboration;
using Adventures.MEF;

namespace Adventures.Hosting;

internal static class AppEntityServices
{
    public static IServiceCollection AddAppEntities(this IServiceCollection services, IReadOnlyList<NQuad> userQuads, IConfiguration configuration, bool isDevelopment)
    {
        // Each feature registers its own entity repositories (Adventures.Entities.NQuad, R2 of the host reuse review): the host names the features it uses.
        services.AddCoreEntities(userQuads);
        services.AddDiagnosticsEntities(userQuads);
        services.AddIdentityEntities(userQuads);
        // Start-up tasks of the identity feature (role-rights upgrade, first platform administrator), run after the seeds by StartupTaskRunner.
        services.AddIdentityStartupTasks();
        services.AddBlogEntities(userQuads);
        services.AddCollaborationEntities(userQuads);
        services.AddPluginEntities(userQuads);
        // The platform administrator is a switch on the person (not a role, not tied to a tenant); only an existing platform administrator changes it.
        services.AddNQuadPlatformAdministrators();
        services.AddNQuadEmailVerification();
        services.AddPasswordReset();
        services.AddRecovery();
        services.AddScoped<ITenantAliasEditor, TenantAliasEditor>();
        services.AddScoped<ITenantRemover, TenantRemover>();
        services.AddScoped<ITenantCreator, TenantCreator>();
        services.AddScoped<ITenantFirstRun, TenantFirstRun>();
        // The first site of a fresh install needs the sign-in store; the identity services register the real one, a host without it gets this no-op (the middleware always asks).
        services.TryAddScoped<IFirstRunSite, NoFirstRunSite>();
        services.TryAddScoped<ISiteProvisioner, NoSiteProvisioner>();
        // The tenant switcher: a person acts in the tenants they belong to (a platform administrator in every one); the X-Acting-Tenant header is checked on every request.
        services.AddScoped<IActingTenants, ActingTenants>();
        services.AddScoped<IRightsResolver, RightsResolver>();
        services.AddScoped<IRoleRightsEditor, RoleRightsEditor>();
        services.AddRightsAuthorization();
        services.AddScoped<IEntityBll<Tenant>>(provider => new EntityBll<Tenant>(provider.GetRequiredService<IEntityDal<Tenant>>()));
        services.AddScoped<IEntityBll<Organization>>(provider => new EntityBll<Organization>(provider.GetRequiredService<IEntityDal<Organization>>()));
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantHostSource, TenantHostSource>();
        // T1: the tenant of each request is decided from the host the server saw (Tenancy:Enforcement = Off, Report (default) or Enforce; Tenancy:FallbackDomain only in Development).
        services.AddTenancy(configuration, isDevelopment);
        // T2: posts, pages, categories, tags, comments, collaboration items and site settings live in the request tenant's own graph; every tenant is equal and a host with no tenant sees none of it.
        services.AddScoped<IGraphScope, RequestGraphScope>();
        // T3b: the people screen shows and changes only members of the request tenant; sign-in and the profile keep the plain, global IUserBll.
        services.AddScoped<ITenantContext, RequestGraphScope>();
        services.AddNQuadUserMembership();
        services.AddScoped<UserVisibility>();
        services.AddScoped<PlatformUserVisibilityFilter>();
        services.AddScoped<TenantScopedUserBll>();
        services.AddScoped<ITenantUserPresenter, TenantUserPresenter>();
        // E-mail: a mail server on a private network is refused unless the operator allows it (Mail:AllowPrivateHosts).
        services.AddSingleton(new MailOptions { AllowPrivateHosts = configuration.GetValue<bool>("Mail:AllowPrivateHosts") });
        // The hosts a tracking script may come from (Site:AllowedScriptHosts); none means no tracking script is ever served.
        services.AddSingleton(new SiteOptions { AllowedScriptHosts = configuration.GetSection("Site:AllowedScriptHosts").Get<string[]>() ?? [] });
        services.AddBlog();
        services.AddCollaboration();
        services.AddNQuadRelations();
        services.AddNQuadRelationWriter();
        services.AddIdentityRelationGuards();
        services.AddScoped<IEntityBll<Role>>(provider => new EntityBll<Role>(provider.GetRequiredService<IEntityDal<Role>>()));
        services.AddScoped<IEntityPresenter<Role>>(provider => new RolePresenter(
            provider.GetRequiredService<IEntityBll<Role>>(), provider.GetRequiredService<EntitySchemaOf<Role>>(), provider.GetService<IEntityRelationProvider>()));
        return services;
    }
}
