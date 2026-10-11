using Adventures.Common.Interfaces;

namespace Adventures.Entities;

/// <summary>
/// The generic business layer for any <see cref="DynamicEntity"/> type: CRUDL plus the rules every
/// entity shares (required fields). Sits between a presenter and <see cref="IEntityRepository{TEntity}"/>
/// (the Dal) so a presenter never reaches the Dal directly (AppArch 3/10/11; MVPVM: the Presenter's
/// dependencies are Bll interfaces). An entity with real rules of its own derives from
/// <see cref="EntityBll{TEntity}"/> and exposes a narrower interface (e.g. <see cref="IUserBll"/>), which
/// is also what lets a versioned or alternate implementation be swapped in by DI.
/// Open generics are registered by the host with <c>AddEntityBll()</c>; the Ioc reflection scan only
/// sees closed implementations such as <see cref="UserBll"/>.
/// </summary>
public interface IEntityBll<TEntity> : IBll
    where TEntity : DynamicEntity
{
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<TEntity?> GetAsync(string entityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default);
}
