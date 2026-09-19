using Adventures.Data;

namespace Adventures.Data.Tests;

/// <summary>
/// Hand-rolled fake <see cref="ISqlExecutor"/> for TDD-ing repository/store logic without a live
/// PostgreSQL instance. Records every call and lets tests script canned results per-call.
/// </summary>
internal sealed class FakeSqlExecutor : ISqlExecutor
{
    public List<(string Sql, object? Parameters)> ExecuteCalls { get; } = [];
    public List<(string Sql, object? Parameters)> QueryCalls { get; } = [];
    public List<(string Sql, object? Parameters)> QuerySingleCalls { get; } = [];

    public int ExecuteResult { get; set; } = 1;
    public Func<string, object?, object?>? QuerySingleOrDefaultHandler { get; set; }
    public Func<string, object?, object?>? QueryHandler { get; set; }

    public Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        ExecuteCalls.Add((sql, parameters));
        return Task.FromResult(ExecuteResult);
    }

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        QuerySingleCalls.Add((sql, parameters));
        var result = QuerySingleOrDefaultHandler?.Invoke(sql, parameters);
        return Task.FromResult((T?)result);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        QueryCalls.Add((sql, parameters));
        var result = QueryHandler?.Invoke(sql, parameters) as IReadOnlyList<T> ?? [];
        return Task.FromResult(result);
    }
}
