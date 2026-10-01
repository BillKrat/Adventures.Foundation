using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.WebApi;

/// <summary>
/// Shares the generic CRUDL action *bodies* for any <see cref="IEntityPresenter{TEntity}"/>-backed
/// entity, as protected helpers - not <c>[Http*]</c>-decorated action methods themselves. A fully
/// generic MVC controller (an open generic type, route-templated by <c>TEntity</c>) fights
/// Swagger/attribute routing, so each app still writes its own small, concrete, attribute-decorated
/// controller; this is the part of that controller that would otherwise be copy-pasted. Delete is
/// deliberately not provided here - every real entity's delete needs at least an acting-user id for
/// audit/authorization, which does not generalize the same way (see <see cref="IUserPresenter"/>'s
/// guarded overload) - a concrete controller calls its presenter's own Delete directly, wrapped in
/// <see cref="ApiControllerBase.GuardedAsync"/>.
/// </summary>
public abstract class EntityControllerBase<TEntity> : ApiControllerBase
    where TEntity : DynamicEntity
{
    protected async Task<ActionResult<EntityFormModel>> GetAsync(
        IEntityPresenter<TEntity> presenter, string id, CancellationToken cancellationToken)
    {
        var form = await presenter.GetFormAsync(id, cancellationToken).ConfigureAwait(false);
        return form is null ? NotFound() : Ok(form);
    }

    protected async Task<ActionResult<IReadOnlyList<EntityDataModel>>> ListAsync(
        IEntityPresenter<TEntity> presenter, CancellationToken cancellationToken) =>
        Ok(await presenter.ListAsync(cancellationToken).ConfigureAwait(false));

    protected async Task<ActionResult<EntityFormModel>> CreateAsync(
        IEntityPresenter<TEntity> presenter, EntityDataModel data, CancellationToken cancellationToken)
    {
        var created = await presenter.CreateAsync(data, cancellationToken).ConfigureAwait(false);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    protected async Task<ActionResult<EntityFormModel>> UpdateAsync(
        IEntityPresenter<TEntity> presenter, string id, EntityDataModel data, CancellationToken cancellationToken)
    {
        var updated = await presenter.UpdateAsync(id, data, cancellationToken).ConfigureAwait(false);
        return updated is null ? NotFound() : Ok(updated);
    }
}
