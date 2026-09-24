using Dapper;
using Npgsql;

namespace Adventures.Data.NQuad;

/// <summary>
/// Real PostgreSQL-backed store for <see cref="NQuad"/> rows in the "n_quads" table (see
/// Sql/schema-nquads.sql). Deliberately self-contained (no dependency on Adventures.Data/
/// ISqlExecutor) so this library can evolve in parallel with, and eventually replace, the
/// existing JSONB Hybrid + N-Quads model without either biasing the other. Also doubles as
/// the "tool" used by Adventures.Data.NQuad.Tests to create/purge/reseed/query during
/// red-green development.
/// </summary>
public sealed class NpgsqlNQuadStore(string connectionString)
{
    private const string CreateTableSql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;

        CREATE TABLE IF NOT EXISTS n_quads (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            subject TEXT NOT NULL,
            predicate TEXT NOT NULL,
            object TEXT NOT NULL,
            graph TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS ix_n_quads_subject ON n_quads (subject);
        CREATE INDEX IF NOT EXISTS ix_n_quads_predicate ON n_quads (predicate);
        CREATE INDEX IF NOT EXISTS ix_n_quads_graph ON n_quads (graph);
        """;

    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <summary>Creates the "n_quads" table (and its indexes) if it does not already exist.</summary>
    public async Task CreateTableAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(CreateTableSql, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    /// <summary>Deletes every row from "n_quads", leaving the table itself intact.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition("TRUNCATE TABLE n_quads;", cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    /// <summary>Inserts a single quad.</summary>
    public Task<int> InsertAsync(NQuad quad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quad);
        return InsertManyAsync([quad], cancellationToken);
    }

    /// <summary>Inserts a batch of quads in a single round trip.</summary>
    public async Task<int> InsertManyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        if (quads.Count == 0)
        {
            return 0;
        }

        const string sql = """
            INSERT INTO n_quads (id, subject, predicate, object, graph)
            VALUES (@Id, @Subject, @Predicate, @Object, @Graph);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, quads, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    /// <summary>
    /// Parses a ".nq" file (see Sql/seed/seed.nq) and inserts every quad it contains. Returns the
    /// number of quads seeded.
    /// </summary>
    public async Task<int> SeedFromFileAsync(string nQuadFilePath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(nQuadFilePath, cancellationToken);
        var quads = new NQuadFileParser().Parse(text);
        return await InsertManyAsync(quads, cancellationToken);
    }

    /// <summary>Returns every row in "n_quads", optionally filtered by any combination of terms.</summary>
    public async Task<IReadOnlyList<NQuad>> QueryAsync(
        string? subject = null,
        string? predicate = null,
        string? @object = null,
        string? graph = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id AS "Id", subject AS "Subject", predicate AS "Predicate", object AS "Object", graph AS "Graph"
            FROM n_quads
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

    /// <summary>Returns the total row count in "n_quads".</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition("SELECT COUNT(*) FROM n_quads;", cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<long>(command);
    }
}

