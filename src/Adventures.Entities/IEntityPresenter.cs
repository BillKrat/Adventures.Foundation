using Adventures.Common.Interfaces;

namespace Adventures.Entities;

/// <summary>
/// Resolved by a controller (scoped, interface-driven, auto-registered the same way
/// <c>MockBll</c>/<c>MockDal</c> are in Adventures.Common.Tests/MvpVmTests.cs). Orchestrates an
/// <see cref="IEntityRepository{TEntity}"/> plus the entity's <see cref="EntitySchema"/> into the
/// "standard object" (<see cref="EntityFormModel"/>) a UI form control renders from - generic over
/// any <see cref="DynamicEntity"/> type, with no business rules of its own (that is a Bll's job,
/// the same split <see cref="IEntityRepository{TEntity}"/> already draws against
/// <c>NQuadEntityRepository{TEntity}</c>). Delete here is deliberately ungated - an entity type
/// with a real guard (e.g. <see cref="IUserPresenter"/>'s "cannot delete your own account") adds a
/// same-named overload rather than hiding or overriding this one.
/// </summary>
public interface IEntityPresenter<TEntity> : IPresenter
    where TEntity : DynamicEntity
{
    Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default);

    Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default);

    Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
