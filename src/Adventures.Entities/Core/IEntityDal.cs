namespace Adventures.Entities;

/// <summary>
/// The data-access layer a Bll talks to: CRUDL over <see cref="DynamicEntity"/>-derived types, with the semantics of
/// <see cref="IEntityRepository{TEntity}"/> (delete tombstones, update supersedes, nothing is physically removed). A Bll depends on
/// this and never on a repository, so the shape of the data (N-Quads today, the legacy SQL tables later) is decided by which Dal is
/// registered. A repository is a tool the default <see cref="EntityDal{TEntity}"/> uses; it does not stand in for the Dal.
/// </summary>
public interface IEntityDal<TEntity>
    where TEntity : DynamicEntity
{
    /// <summary>Inserts a new entity. Throws if an entity with the same id already exists, live or deleted.</summary>
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Reads one entity by id, or null; a tombstoned entity counts as missing unless <paramref name="includeDeleted"/> is true.</summary>
    Task<TEntity?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Reads every entity of this type; tombstoned ones are excluded unless <paramref name="includeDeleted"/> is true.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Counts the entities of this type without reading them.</summary>
    Task<int> CountAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Writes every dirty field of <paramref name="entity"/>; throws if it does not exist or is deleted.</summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Tombstones the entity; false if it did not exist or was already deleted.</summary>
    Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>Tombstones every listed entity that exists, in one batch; returns how many were deleted.</summary>
    Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default);
}
