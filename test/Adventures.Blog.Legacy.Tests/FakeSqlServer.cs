using System.Reflection;
using Adventures.Data.Sql;

namespace Adventures.Blog.Legacy.Tests;

/// <summary>A scripted SQL Server: answers a query by the first registered fragment the SQL contains, records every statement, and returns the rows-affected a test sets.</summary>
internal sealed class FakeSqlServer : ISqlServerExecutor
{
    private readonly List<(string Fragment, Func<object?, object?> Answer)> _answers = [];

    public List<(string Sql, object? Parameters)> Statements { get; } = [];

    public int RowsAffected { get; set; } = 1;

    public void Answer(string fragment, Func<object?, object?> rows) => _answers.Add((fragment, rows));

    public void Answer(string fragment, object? rows) => _answers.Add((fragment, _ => rows));

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        Statements.Add((sql, parameters));
        return Task.FromResult<IReadOnlyList<T>>(Find(sql, parameters) is IEnumerable<T> rows ? rows.ToList() : []);
    }

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        Statements.Add((sql, parameters));
        return Task.FromResult(Find(sql, parameters) is T single ? single : Find(sql, parameters) is IEnumerable<T> many ? many.FirstOrDefault() : default);
    }

    public Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        Statements.Add((sql, parameters));
        return Task.FromResult(RowsAffected);
    }

    public IEnumerable<(string Sql, object? Parameters)> Executed(string startsWith) =>
        Statements.Where(s => s.Sql.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase));

    public static object? Param(object? parameters, string name) => parameters?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(parameters);

    private object? Find(string sql, object? parameters) => _answers.FirstOrDefault(a => sql.Contains(a.Fragment, StringComparison.OrdinalIgnoreCase)).Answer?.Invoke(parameters);
}
