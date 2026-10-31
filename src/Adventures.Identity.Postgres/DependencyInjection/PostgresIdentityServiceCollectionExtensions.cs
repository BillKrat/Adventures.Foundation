using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Identity.Postgres;

public static class PostgresIdentityServiceCollectionExtensions
{
    /// <summary>Stores user credentials (hash, lockout, last login) in the PostgreSQL vault table. The host also registers <c>ISqlExecutor</c> (Adventures.Data.Postgres).</summary>
    public static IServiceCollection AddPostgresUserCredentials(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IUserCredentialStore, PostgresUserCredentialStore>();
        return services;
    }
}
