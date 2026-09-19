namespace Adventures.Data;

/// <summary>
/// PostgreSQL-backed <see cref="ITripleStore"/> implementation, operating against the
/// "entity_triples" table (W3C N-Quads side of the JSONB Hybrid + N-Quads storage model).
/// All SQL execution goes through <see cref="ISqlExecutor"/> so this class can be unit tested
/// with a mocked executor, without a live PostgreSQL instance.
/// </summary>
public sealed class PostgresTripleStore(ISqlExecutor executor) : ITripleStore
{
    private readonly ISqlExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public Task AddTripleAsync(Triple triple, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triple);

        const string sql = """
            INSERT INTO entity_triples (subject, predicate, object, object_datatype, graph)
            VALUES (@Subject, @Predicate, @Object, @ObjectDatatype, @Graph)
            ON CONFLICT (subject, predicate, graph)
            DO UPDATE SET object = EXCLUDED.object, object_datatype = EXCLUDED.object_datatype
            """;

        return _executor.ExecuteAsync(sql, triple, cancellationToken);
    }

    public Task<IReadOnlyList<Triple>> GetTriplesAsync(Guid subject, string? graph = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT subject AS Subject, predicate AS Predicate, object AS Object,
                   object_datatype AS ObjectDatatype, graph AS Graph
            FROM entity_triples
            WHERE subject = @Subject
              AND (@Graph IS NULL OR graph = @Graph)
            """;

        return _executor.QueryAsync<Triple>(sql, new { Subject = subject, Graph = graph }, cancellationToken);
    }

    public async Task<bool> RemoveTripleAsync(Guid subject, string predicate, string graph, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM entity_triples
            WHERE subject = @Subject AND predicate = @Predicate AND graph = @Graph
            """;

        var affected = await _executor.ExecuteAsync(sql, new { Subject = subject, Predicate = predicate, Graph = graph }, cancellationToken);
        return affected > 0;
    }
}
