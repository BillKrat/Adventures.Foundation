// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// Default <see cref="IEntityBll{TEntity}"/>: delegates storage to <see cref="IEntityRepository{TEntity}"/> and
/// enforces the one rule every entity shares - a field the schema marks required must hold a value on create and
/// update. Derive to add entity-specific rules; override <see cref="Validate"/> or the CRUDL members and call base.
/// </summary>
public class EntityBll<TEntity>(IEntityRepository<TEntity> repository) : IEntityBll<TEntity>
    where TEntity : DynamicEntity
{
    protected IEntityRepository<TEntity> Repository { get; } =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public virtual Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Validate(entity);
        return Repository.CreateAsync(entity, cancellationToken);
    }

    public virtual Task<TEntity?> GetAsync(string entityId, CancellationToken cancellationToken = default) =>
        Repository.GetAsync(entityId, cancellationToken: cancellationToken);

    public virtual Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default) =>
        Repository.ListAsync(cancellationToken: cancellationToken);

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Validate(entity);
        return Repository.UpdateAsync(entity, cancellationToken);
    }

    public virtual Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        Repository.DeleteAsync(entityId, cancellationToken);

    public virtual Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) =>
        Repository.DeleteManyAsync(entityIds, cancellationToken);

    /// <summary>Throws <see cref="InputValidationException"/> listing every required field that has no value.</summary>
    protected virtual void Validate(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var errors = entity.Fields
            .Where(field => field.IsRequired
                && !field.Name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase)
                && field.Value is null)
            .ToDictionary(field => field.Name, field => new[] { $"{field.Name} is required." });

        if (errors.Count > 0)
        {
            throw new InputValidationException(errors);
        }
    }
}
