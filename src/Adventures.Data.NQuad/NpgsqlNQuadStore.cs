// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
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

    /// <summary>Leading characters of "object" covered by the (predicate, object) index; see <see cref="QueryAsync"/>.</summary>
    private const int ObjectIndexLength = 255;

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
            CREATE INDEX IF NOT EXISTS ix_{_table}_subject_predicate ON {_table} (subject, predicate);
            CREATE INDEX IF NOT EXISTS ix_{_table}_predicate_object ON {_table} (predicate, left(object, {ObjectIndexLength}));
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

        // One statement, one round trip, however many quads: the columns go up as arrays and unnest() turns them back into
        // rows. (Handing Dapper a list runs the INSERT once per element - a round trip each, which against a remote
        // database made saving or purging a handful of entities take tens of seconds.) A single statement is atomic,
        // so a duplicate id still adds nothing.
        var sql = $"""
            INSERT INTO {_table} (id, subject, predicate, object, graph)
            SELECT * FROM unnest(@Ids, @Subjects, @Predicates, @Objects, @Graphs);
            """;

        var parameters = new
        {
            Ids = quads.Select(q => q.Id).ToArray(),
            Subjects = quads.Select(q => q.Subject).ToArray(),
            Predicates = quads.Select(q => q.Predicate).ToArray(),
            Objects = quads.Select(q => q.Object).ToArray(),
            Graphs = quads.Select(q => q.Graph).ToArray(),
        };

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    /// <summary>Returns every row, optionally filtered by any combination of terms.</summary>
    public async Task<IReadOnlyList<NQuad>> QueryAsync(string? subject = null, string? predicate = null, string? @object = null, string? graph = null, CancellationToken cancellationToken = default)
    {
        // Only the terms actually supplied become conditions. The "(@X IS NULL OR col = @X)" form would stop PostgreSQL
        // choosing an index once it settles on a generic plan. The object term also carries a left(object, N) equality
        // so the (predicate, left(object, N)) expression index applies: object can be an arbitrarily long literal (a
        // post body), which cannot go in a b-tree whole.
        var conditions = new List<string>();
        if (subject is not null) conditions.Add("subject = @Subject");
        if (predicate is not null) conditions.Add("predicate = @Predicate");
        if (@object is not null) conditions.Add($"left(object, {ObjectIndexLength}) = left(@Object, {ObjectIndexLength}) AND object = @Object");
        if (graph is not null) conditions.Add("graph = @Graph");

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        var sql = $"""
            SELECT id AS "Id", subject AS "Subject", predicate AS "Predicate", object AS "Object", graph AS "Graph"
            FROM {_table}
            {where};
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { Subject = subject, Predicate = predicate, Object = @object, Graph = graph }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<NQuad>(command);
        return results.AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NQuad>> QueryBySubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        cancellationToken.ThrowIfCancellationRequested();
        if (subjects.Count == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT id AS "Id", subject AS "Subject", predicate AS "Predicate", object AS "Object", graph AS "Graph"
            FROM {_table}
            WHERE subject = ANY(@Subjects);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { Subjects = subjects.ToArray() }, cancellationToken: cancellationToken);
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
