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

    /// <summary>
    /// One sorted, paged grid of the entities, with the links the client follows (self, create, next, previous) built from
    /// this request path, so the client holds no URL templates. The query comes from <c>?page&amp;pageSize&amp;sort&amp;descending</c>.
    /// </summary>
    protected async Task<ActionResult<EntityGridModel>> GridAsync(
        IEntityPresenter<TEntity> presenter, EntityGridQuery query, CancellationToken cancellationToken)
    {
        var grid = await presenter.GetGridAsync(query, cancellationToken).ConfigureAwait(false);
        return Ok(grid.WithLinks(BuildGridLinks(grid, Request.Path.Value ?? string.Empty)));
    }

    /// <summary>Builds the grid links for <paramref name="path"/> (for example <c>/api/users</c>); relative to the site root, query string included.</summary>
    public static EntityGridLinks BuildGridLinks(EntityGridModel grid, string path)
    {
        var paging = grid.Paging;
        if (paging is null)
        {
            return new EntityGridLinks(path, grid.CanCreate ? path : null);
        }

        string Page(int number) =>
            $"{path}?page={number}&pageSize={paging.PageSize}"
            + (paging.Sort is null ? string.Empty : $"&sort={Uri.EscapeDataString(paging.Sort)}&descending={paging.Descending.ToString().ToLowerInvariant()}");

        return new EntityGridLinks(
            Page(paging.Page),
            grid.CanCreate ? path : null,
            paging.Page < paging.TotalPages ? Page(paging.Page + 1) : null,
            paging.Page > 1 ? Page(paging.Page - 1) : null);
    }

    protected async Task<ActionResult<IReadOnlyList<EntityDataModel>>> ListAsync(
        IEntityPresenter<TEntity> presenter, CancellationToken cancellationToken) =>
        Ok(await presenter.ListAsync(cancellationToken).ConfigureAwait(false));

    protected Task<ActionResult<EntityFormModel>> CreateAsync(
        IEntityPresenter<TEntity> presenter, EntityDataModel data, CancellationToken cancellationToken) =>
        GuardedAsync<EntityFormModel>(async () =>
        {
            var created = await presenter.CreateAsync(data, cancellationToken).ConfigureAwait(false);
            return StatusCode(StatusCodes.Status201Created, created);
        });

    protected Task<ActionResult<EntityFormModel>> UpdateAsync(
        IEntityPresenter<TEntity> presenter, string id, EntityDataModel data, CancellationToken cancellationToken) =>
        GuardedAsync<EntityFormModel>(async () =>
        {
            var updated = await presenter.UpdateAsync(id, data, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(updated);
        });
}
