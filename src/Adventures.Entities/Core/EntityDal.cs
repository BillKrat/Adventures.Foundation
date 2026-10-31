namespace Adventures.Entities;

/// <summary>
/// Default <see cref="IEntityDal{TEntity}"/>: uses an <see cref="IEntityRepository{TEntity}"/> as its storage tool. This is the only
/// place in <c>Adventures.Entities</c> that knows about a repository. Derive (or register another Dal) to change how an entity type
/// is stored or shaped without touching its Bll.
/// </summary>
public class EntityDal<TEntity>(IEntityRepository<TEntity> repository) : IEntityDal<TEntity>
    where TEntity : DynamicEntity
{
    protected IEntityRepository<TEntity> Repository { get; } =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public virtual Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        Repository.CreateAsync(entity, cancellationToken);

    public virtual Task<TEntity?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        Repository.GetAsync(entityId, includeDeleted, cancellationToken);

    public virtual Task<IReadOnlyList<TEntity>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        Repository.ListAsync(includeDeleted, cancellationToken);

    public virtual Task<int> CountAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        Repository.CountAsync(includeDeleted, cancellationToken);

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        Repository.UpdateAsync(entity, cancellationToken);

    public virtual Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        Repository.DeleteAsync(entityId, cancellationToken);

    public virtual Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) =>
        Repository.DeleteManyAsync(entityIds, cancellationToken);
}
