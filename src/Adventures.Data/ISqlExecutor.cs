namespace Adventures.Data;

/// <summary>
/// A thin, mockable seam over SQL execution. Repository implementations depend on this interface
/// rather than on Npgsql/Dapper types directly, so unit tests can substitute a fake executor and
/// exercise CRUDL/triple-store logic without a live PostgreSQL instance.
/// </summary>
public interface ISqlExecutor
{
    /// <summary>Executes a query and maps each row to <typeparamref name="T"/>.</summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Executes a query expected to return at most one row, or <c>null</c> if none match.</summary>
    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Executes a non-query statement (INSERT/UPDATE/DELETE) and returns the affected row count.</summary>
    Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
}
