namespace Adventures.Data;

/// <summary>
/// PostgreSQL-backed <see cref="IEntityRepository"/> implementation, operating against the
/// "entities" table (JSONB "standard fields" side of the JSONB Hybrid + N-Quads storage model).
/// All SQL execution goes through <see cref="ISqlExecutor"/> so this class can be unit tested
/// with a mocked executor, without a live PostgreSQL instance.
/// </summary>
public sealed class PostgresEntityRepository(ISqlExecutor executor) : IEntityRepository
{
    private readonly ISqlExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<Entity> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var now = DateTimeOffset.UtcNow;
        var toInsert = entity with { CreatedAt = now, UpdatedAt = now, RowVersion = 1 };

        const string sql = """
            INSERT INTO entities (id, tenant, org, entity_type, standard_fields, created_at, updated_at, row_version)
            VALUES (@Id, @Tenant, @Org, @EntityType, @StandardFieldsJson::jsonb, @CreatedAt, @UpdatedAt, @RowVersion)
            """;

        await _executor.ExecuteAsync(sql, toInsert, cancellationToken);
        return toInsert;
    }

    public Task<Entity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id AS Id, tenant AS Tenant, org AS Org, entity_type AS EntityType,
                   standard_fields::text AS StandardFieldsJson, created_at AS CreatedAt,
                   updated_at AS UpdatedAt, row_version AS RowVersion
            FROM entities
            WHERE id = @Id
            """;

        return _executor.QuerySingleOrDefaultAsync<Entity>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        const string sql = """
            UPDATE entities
            SET tenant = @Tenant, org = @Org, entity_type = @EntityType,
                standard_fields = @StandardFieldsJson::jsonb, updated_at = @UpdatedAt, row_version = row_version + 1
            WHERE id = @Id AND row_version = @RowVersion
            """;

        var affected = await _executor.ExecuteAsync(sql, entity with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM entities WHERE id = @Id";
        var affected = await _executor.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return affected > 0;
    }

    public Task<IReadOnlyList<Entity>> ListAsync(EntityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string sql = """
            SELECT id AS Id, tenant AS Tenant, org AS Org, entity_type AS EntityType,
                   standard_fields::text AS StandardFieldsJson, created_at AS CreatedAt,
                   updated_at AS UpdatedAt, row_version AS RowVersion
            FROM entities
            WHERE (@Tenant IS NULL OR tenant = @Tenant)
              AND (@Org IS NULL OR org = @Org)
              AND (@EntityType IS NULL OR entity_type = @EntityType)
            ORDER BY created_at
            OFFSET @Skip LIMIT @Take
            """;

        return _executor.QueryAsync<Entity>(sql, query, cancellationToken);
    }

    public Task<Entity?> FindByStandardFieldAsync(string tenant, string entityType, string fieldName, string fieldValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldValue);

        const string sql = """
            SELECT id AS Id, tenant AS Tenant, org AS Org, entity_type AS EntityType,
                   standard_fields::text AS StandardFieldsJson, created_at AS CreatedAt,
                   updated_at AS UpdatedAt, row_version AS RowVersion
            FROM entities
            WHERE tenant = @Tenant AND entity_type = @EntityType AND standard_fields ->> @FieldName = @FieldValue
            LIMIT 1
            """;

        return _executor.QuerySingleOrDefaultAsync<Entity>(
            sql,
            new { Tenant = tenant, EntityType = entityType, FieldName = fieldName, FieldValue = fieldValue },
            cancellationToken);
    }
}
