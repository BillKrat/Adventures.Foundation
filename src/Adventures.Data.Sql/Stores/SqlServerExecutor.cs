using Adventures.Common.Diagnostics;
using Adventures.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Adventures.Data.Sql;

/// <summary>An <see cref="ISqlExecutor"/> over SQL Server, distinct from the PostgreSQL one so a host can hold both without ambiguity.</summary>
public interface ISqlServerExecutor : ISqlExecutor;

/// <summary>SQL Server <see cref="ISqlExecutor"/>: a new pooled connection per call, parameters always bound (never concatenated).</summary>
public sealed class SqlServerExecutor(string connectionString) : ISqlServerExecutor
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}

/// <summary>The SQL Server store's <see cref="IStoreProbe"/>: one trivial query.</summary>
public sealed class SqlServerStoreProbe(ISqlServerExecutor executor) : IStoreProbe
{
    public Task CheckAsync(CancellationToken cancellationToken = default) =>
        executor.QuerySingleOrDefaultAsync<int>("SELECT 1", null, cancellationToken);
}
