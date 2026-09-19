namespace Adventures.Data;

/// <summary>
/// Generic CRUDL access to <see cref="Entity"/> records, backed by the "standard fields" JSONB
/// side of the JSONB Hybrid + N-Quads storage model. See <see cref="ITripleStore"/> for the
/// companion interface covering fully user-defined/extension fields.
/// </summary>
public interface IEntityRepository
{
    /// <summary>Inserts a new entity and returns the stored record (with server-assigned timestamps/row version).</summary>
    Task<Entity> CreateAsync(Entity entity, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a single entity by id, or <c>null</c> if it does not exist.</summary>
    Task<Entity?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing entity's standard fields. <paramref name="entity"/>.RowVersion must be the
    /// version last read by the caller (optimistic concurrency) — the update is applied only if the
    /// stored row still has that version. Returns <c>false</c> if no entity with that id exists <em>or</em>
    /// if it exists but was modified since, changing its row_version (a concurrency conflict) — the two
    /// cases aren't distinguished by the return value; callers that need to tell them apart should
    /// <see cref="GetAsync"/> the current row afterward.
    /// </summary>
    Task<bool> UpdateAsync(Entity entity, CancellationToken cancellationToken = default);

    /// <summary>Deletes an entity by id. Returns <c>false</c> if no entity with that id existed.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists entities matching the given filter criteria.</summary>
    Task<IReadOnlyList<Entity>> ListAsync(EntityQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a single entity within a tenant whose <c>standard_fields</c> JSONB has
    /// <paramref name="fieldName"/> set to <paramref name="fieldValue"/> - e.g. finding a user by
    /// username at login time via the partial index on <c>standard_fields ->> 'username'</c> (see
    /// <c>Sql/schema-users.sql</c>). Returns <c>null</c> if no match. Callers are responsible for
    /// the field actually being unique within (tenant, entityType); if more than one row matches,
    /// which one is returned is unspecified.
    /// </summary>
    Task<Entity?> FindByStandardFieldAsync(string tenant, string entityType, string fieldName, string fieldValue, CancellationToken cancellationToken = default);
}
