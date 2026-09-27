namespace Adventures.Entities;

/// <summary>
/// CRUDL over <see cref="DynamicEntity"/>-derived types - the counterpart to a store's own maintenance interface
/// (e.g. <c>INQuadStore</c> in <c>Adventures.Data.NQuad</c>), which this depends on but never appears in this
/// contract: store maintenance and entity CRUDL are deliberately separate concerns. Delete is a tombstone and
/// Update supersedes rather than overwrites - nothing here is ever physically removed, so a store-maintenance
/// implementation of this interface never needs a delete-by-id or update-by-id operation of its own. This
/// interface has no business rules of any kind (an entity's Bll - e.g. a future <c>SchemaBll</c> - is where those
/// live); it only knows how to read, write, and track the lifecycle of whatever schema it was built for.
/// </summary>
public interface IEntityRepository<TEntity>
    where TEntity : DynamicEntity
{
    /// <summary>Inserts a new entity. Throws if an entity with the same id already exists, live or deleted.</summary>
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one entity by id, or null if it does not exist. A tombstoned entity is treated as not existing unless
    /// <paramref name="includeDeleted"/> is true.
    /// </summary>
    Task<TEntity?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Reads every entity of this type. A tombstoned entity is excluded unless <paramref name="includeDeleted"/> is true.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes every dirty field on <paramref name="entity"/> (see <see cref="DynamicEntity.DirtyFieldIds"/>): each
    /// prior quad for a changed field is marked superseded (never removed) and a new quad is inserted with the new
    /// value. Throws if the entity does not exist or is currently deleted.
    /// </summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tombstones the entity: nothing is physically removed. Returns false if the entity did not exist or was
    /// already deleted.
    /// </summary>
    Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default);
}
