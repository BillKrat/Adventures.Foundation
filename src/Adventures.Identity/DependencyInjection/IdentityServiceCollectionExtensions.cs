// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Identity;

/// <summary>Registers the user-login building blocks for a host application.</summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUserAccountService"/> and <see cref="IUserProvisioning"/>. The
    /// host must separately register an <see cref="IUserCredentialStore"/> (e.g. <c>AddPostgresUserCredentials</c> in <c>Adventures.Identity.Postgres</c>)
    /// and <c>Adventures.Data</c>'s <c>IEntityRecordRepository</c> (e.g. a real Postgres connection) and <c>Adventures.Security</c>'s <c>IPasswordHasher</c>/
    /// <c>IJwtTokenService</c> (via <c>AddSharedJwtAuthentication</c>) - this method only wires up
    /// the identity-specific pieces on top of those.
    /// </summary>
    public static IServiceCollection AddUserIdentity(this IServiceCollection services)
    {
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        return services;
    }
}
