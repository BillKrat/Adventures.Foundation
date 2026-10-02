namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IEntityPresenter{TEntity}"/> - see that interface for the contract and rationale.</summary>
public class EntityPresenter<TEntity>(
    IEntityRepository<TEntity> repository,
    EntitySchema schema,
    Func<EntitySchema, TEntity> entityFactory) : IEntityPresenter<TEntity>
    where TEntity : DynamicEntity
{
    protected IEntityRepository<TEntity> Repository { get; } = repository ?? throw new ArgumentNullException(nameof(repository));

    protected EntitySchema Schema { get; } = schema ?? throw new ArgumentNullException(nameof(schema));

    protected Func<EntitySchema, TEntity> EntityFactory { get; } = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));

    public async Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entity is null ? null : EntityFormModel.From(Schema, entity);
    }

    public async Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Repository.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return entities.Select(entity => EntityFormModel.From(Schema, entity).Entity).ToArray();
    }

    public async Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var newId = Guid.NewGuid().ToString();
        var entity = ApplyValues(EntityFactory(Schema), data, newId);
        var created = await Repository.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        return EntityFormModel.From(Schema, created);
    }

    public async Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var existing = await Repository.GetAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        ApplyValues(existing, data, entityId: null);
        await Repository.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        return EntityFormModel.From(Schema, existing);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Repository.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// Applies every value in <paramref name="data"/> except "Id" - the entity id is derived from
    /// the store subject, never a settable field, same convention <c>NQuadEntityRepository</c>
    /// itself follows. When <paramref name="entityId"/> is supplied (Create), sets it first. No
    /// validation yet (declared scope for this stage) - a null or empty-string value is simply
    /// skipped rather than clearing the field. Empty string needs the same treatment as null: a
    /// browser form round-tripping every field has no way to distinguish "never set" from
    /// "cleared" for an input it renders empty, and sends "" either way - without this, a
    /// non-string field (DateOnly, Guid, ...) that happens to be unset throws trying to parse "".
    /// </summary>
    private static TEntity ApplyValues(TEntity entity, EntityDataModel data, string? entityId)
    {
        if (entityId is not null)
        {
            entity.Set(DynamicEntity.EntityIdPropertyName, entityId, entityId);
        }

        foreach (var (name, value) in data.Values)
        {
            if (name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            entity.Set(name, Guid.NewGuid().ToString(), value);
        }

        return entity;
    }
}
