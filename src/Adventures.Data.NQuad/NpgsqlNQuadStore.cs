using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace Adventures.Data.NQuad;

/// <summary>
/// Real PostgreSQL-backed <see cref="INQuadStore"/> for <see cref="NQuad"/> rows in an "n_quads" table (see
/// Sql/schema-nquads.sql). Deliberately self-contained (no dependency on Adventures.Data/
/// ISqlExecutor) so this library can evolve in parallel with, and eventually replace, the
/// existing JSONB Hybrid + N-Quads model without either biasing the other. Also doubles as
/// the "tool" used by Adventures.Data.NQuad.Tests to create/purge/reseed/query during
/// red-green development.
/// </summary>
/// <remarks>
/// <paramref name="tableName"/> defaults to "n_quads". Tests pass a scratch name so contract runs never truncate the dev
/// table. The name is validated as a plain lower-case identifier because it is interpolated into SQL.
/// </remarks>
public sealed partial class NpgsqlNQuadStore : INQuadStore, INQuadStoreInitializer
{
    public const string DefaultTableName = "n_quads";

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex TableNameRegex();

    private readonly string _connectionString;
    private readonly string _table;

    public NpgsqlNQuadStore(string connectionString, string tableName = DefaultTableName)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        ArgumentNullException.ThrowIfNull(tableName);
        if (!TableNameRegex().IsMatch(tableName))
        {
            throw new ArgumentException("Table name must be a lower-case identifier (letters, digits, underscore; max 63 characters).", nameof(tableName));
        }

        _table = tableName;
    }

    /// <summary>Creates the table (and its indexes) if it does not already exist.</summary>
    public async Task CreateTableAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            CREATE EXTENSION IF NOT EXISTS pgcrypto;

            CREATE TABLE IF NOT EXISTS {_table} (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                subject TEXT NOT NULL,
                predicate TEXT NOT NULL,
                object TEXT NOT NULL,
                graph TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS ix_{_table}_subject ON {_table} (subject);
            CREATE INDEX IF NOT EXISTS ix_{_table}_predicate ON {_table} (predicate);
            CREATE INDEX IF NOT EXISTS ix_{_table}_graph ON {_table} (graph);
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) => CreateTableAsync(cancellationToken);

    /// <summary>Deletes every row, leaving the table itself intact.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition($"TRUNCATE TABLE {_table};", cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    /// <summary>Inserts a single quad.</summary>
    public Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quad);
        return InsertManyAsync([quad], cancellationToken);
    }

    /// <summary>Inserts a batch of quads inside one transaction (all-or-nothing).</summary>
    public async Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        cancellationToken.ThrowIfCancellationRequested();
        if (quads.Count == 0)
        {
            return 0;
        }

        var sql = $"""
            INSERT INTO {_table} (id, subject, predicate, object, graph)
            VALUES (@Id, @Subject, @Predicate, @Object, @Graph);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var command = new CommandDefinition(sql, quads, transaction, cancellationToken: cancellationToken);
        var inserted = await connection.ExecuteAsync(command);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    /// <summary>Returns every row, optionally filtered by any combination of terms.</summary>
    public async Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT id AS "Id", subject AS "Subject", predicate AS "Predicate", object AS "Object", graph AS "Graph"
            FROM {_table}
            WHERE (@Subject IS NULL OR subject = @Subject)
              AND (@Predicate IS NULL OR predicate = @Predicate)
              AND (@Object IS NULL OR object = @Object)
              AND (@Graph IS NULL OR graph = @Graph);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { Subject = subject, Predicate = predicate, Object = @object, Graph = graph }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<NQuad>(command);
        return results.AsList();
    }

    /// <summary>Returns the total row count.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition($"SELECT COUNT(*) FROM {_table};", cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<long>(command);
    }
}
