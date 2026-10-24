// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Interfaces;

namespace Adventures.Entities;

/// <summary>
/// Resolved by a controller (scoped, interface-driven, auto-registered the same way
/// <c>MockBll</c>/<c>MockDal</c> are in Adventures.Common.Tests/MvpVmTests.cs). Orchestrates an
/// <see cref="IEntityBll{TEntity}"/> plus the entity's <see cref="EntitySchema"/> into the
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

    /// <summary>An empty form for creating one (schema defaults, no id), or null when this entity type cannot be created from the form.</summary>
    Task<EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The same entities as <see cref="ListAsync"/> with their schema, ready for a grid to render.</summary>
    Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default);

    /// <summary>One sorted, paged slice of the list (see <see cref="EntityGridQuery"/>), with the paging state and what the caller may do. The host adds the links.</summary>
    Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default);

    Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default);

    Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
