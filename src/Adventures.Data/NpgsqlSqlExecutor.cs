using System.Data;
using Dapper;
using Npgsql;

namespace Adventures.Data;

/// <summary>
/// Real PostgreSQL-backed implementation of <see cref="ISqlExecutor"/>, using Npgsql for
/// connections and Dapper for lightweight object mapping. Opens a new connection per call,
/// relying on Npgsql's connection pooling for efficiency.
/// </summary>
public sealed class NpgsqlSqlExecutor(string connectionString) : ISqlExecutor
{
    static NpgsqlSqlExecutor()
    {
        // Npgsql maps "timestamptz" columns to DateTime, but Entity (and other records in this
        // library) use DateTimeOffset for that data - Dapper's constructor-based materialization
        // (used for records with no parameterless constructor, e.g. Entity) matches a reader
        // column's CLR type against constructor parameter types, so without this handler it
        // throws "no parameterless default constructor or one matching signature" even though
        // the column's actual value is always UTC. Never caught before because this project's
        // tests only ever exercised these repositories against a mocked ISqlExecutor, never a
        // live PostgreSQL/Npgsql reader - see docs/SESSION_HANDOFF.md's "mocked, not
        // integration-tested" note for AiBlogResearch.Data (this library's predecessor).
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<T>(command);
        return results.AsList();
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(command);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }
}

/// <summary>Converts a reader's UTC <see cref="DateTime"/> (Npgsql's "timestamptz" mapping) to/from <see cref="DateTimeOffset"/>.</summary>
internal sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateTimeOffset."),
    };

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value;
}
