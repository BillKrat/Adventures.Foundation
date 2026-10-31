using Adventures.Common.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.Sql;

public static class SqlServerServiceCollectionExtensions
{
    /// <summary>Registers the SQL Server executor and its health probe for <paramref name="connectionString"/>. The connection is not opened until something queries.</summary>
    public static IServiceCollection AddSqlServer(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSingleton<ISqlServerExecutor>(_ => new SqlServerExecutor(connectionString));
        services.AddScoped<IStoreProbe, SqlServerStoreProbe>();
        return services;
    }
}
