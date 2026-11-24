using System.Text.RegularExpressions;
using Adventures.Data;
using Adventures.Data.Postgres;
using Xunit;

namespace Adventures.Data.Postgres.Tests;

/// <summary>
/// A fresh install has to create the sign-in tables itself (they used to be made by hand with the scripts in Sql/). The statements only ever CREATE ... IF NOT EXISTS, so running them on
/// every start changes nothing on a database that already has the tables, and a test keeps them equal to the scripts.
/// </summary>
public sealed class IdentitySchemaTests
{
    private sealed class RecordingExecutor : ISqlExecutor
    {
        public List<string> Executed { get; } = [];

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Executed.Add(sql);
            return Task.FromResult(0);
        }
    }

    private static string Normalise(string sql) => Regex.Replace(sql, @"\s+", " ").Trim().TrimEnd(';').Trim();

    private static IEnumerable<string> StatementsOf(string file) =>
        Regex.Replace(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", file)), @"(?m)^\s*--.*$", string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Select(Normalise);

    [Fact]
    public async Task EveryStatementIsRun_InAnOrderThatRespectsTheForeignKeys()
    {
        var executor = new RecordingExecutor();

        await new IdentitySchemaInitializer(executor).EnsureAsync();

        Assert.Equal(IdentitySchema.Statements.Count, executor.Executed.Count);
        var entities = executor.Executed.FindIndex(s => s.Contains("TABLE IF NOT EXISTS entities", StringComparison.Ordinal));
        var credentials = executor.Executed.FindIndex(s => s.Contains("TABLE IF NOT EXISTS user_credentials", StringComparison.Ordinal));
        var triples = executor.Executed.FindIndex(s => s.Contains("TABLE IF NOT EXISTS entity_triples", StringComparison.Ordinal));
        Assert.True(entities >= 0 && credentials > entities && triples > entities);
    }

    [Fact]
    public void EveryStatementIsIdempotent_AndNothingCanDeleteOrChangeData()
    {
        foreach (var statement in IdentitySchema.Statements)
        {
            Assert.Matches(new Regex("^CREATE (TABLE|INDEX|UNIQUE INDEX) IF NOT EXISTS ", RegexOptions.IgnoreCase), Normalise(statement));
            Assert.DoesNotMatch(new Regex(@"\b(DROP|TRUNCATE|DELETE|UPDATE|INSERT|ALTER)\b", RegexOptions.IgnoreCase), statement.Replace("ON DELETE CASCADE", string.Empty));
        }
    }

    [Fact]
    public void TheStatements_AreExactlyTheOnesInTheSqlScripts()
    {
        var fromScripts = StatementsOf("schema.sql").Concat(StatementsOf("schema-users.sql")).ToList();

        Assert.Equal(fromScripts.Select(s => s.ToUpperInvariant()), IdentitySchema.Statements.Select(s => Normalise(s).ToUpperInvariant()));
    }

    [Fact]
    public async Task ARepeatedRun_IsJustTheSameStatementsAgain()
    {
        var executor = new RecordingExecutor();
        var initializer = new IdentitySchemaInitializer(executor);

        await initializer.EnsureAsync();
        await initializer.EnsureAsync();

        Assert.Equal(IdentitySchema.Statements.Count * 2, executor.Executed.Count);
    }
}
