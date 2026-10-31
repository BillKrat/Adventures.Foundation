// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Identity;

/// <summary>Registers the user-login building blocks for a host application.</summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUserCredentialStore"/> and <see cref="IUserAccountService"/>. The
    /// host must separately register <c>Adventures.Data</c>'s <c>ISqlExecutor</c>/<c>IEntityRepository</c>
    /// (e.g. a real Postgres connection) and <c>Adventures.Security</c>'s <c>IPasswordHasher</c>/
    /// <c>IJwtTokenService</c> (via <c>AddSharedJwtAuthentication</c>) - this method only wires up
    /// the identity-specific pieces on top of those.
    /// </summary>
    public static IServiceCollection AddUserIdentity(this IServiceCollection services)
    {
        services.AddScoped<IUserCredentialStore, PostgresUserCredentialStore>();
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        return services;
    }
}
