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

    /// <summary>
    /// Gives each relation grid of a form the links to follow: the URL to add a child (<c>create</c>), the URL to remove each
    /// child (<c>rows</c>), and for each pointer field the URL of its lookup. <paramref name="formPath"/> is the entity URL
    /// (for example <c>/api/users/{id}</c>); relation endpoints hang under it as <c>/relations/{name}</c>.
    /// </summary>
    public static EntityFormModel WithRelationLinks(EntityFormModel form, string formPath, string lookupPath = "/api/lookups")
    {
        if (form.Relations is not { Count: > 0 } relations)
        {
            return form;
        }

        var linked = relations.Select(relation =>
        {
            var basePath = $"{formPath.TrimEnd('/')}/relations/{Uri.EscapeDataString(relation.Name)}";
            var lookups = relation.Grid.Schema.Fields
                .Where(field => field.RefSchema is not null)
                .ToDictionary(field => field.Name, field => $"{lookupPath}?schema={Uri.EscapeDataString(field.RefSchema!)}", StringComparer.OrdinalIgnoreCase);
            var rows = relation.Grid.Rows.ToDictionary(row => row.EntityId, row => $"{basePath}/{Uri.EscapeDataString(row.EntityId)}", StringComparer.Ordinal);
            var links = new EntityGridLinks(basePath, relation.Grid.CanCreate ? basePath : null, Rows: rows, Lookups: lookups);
            return relation with { Grid = relation.Grid.WithLinks(links) };
        }).ToList();
        return form with { Relations = linked };
    }

    /// <summary>Adds a child to one of the parent relations (201 with the child), through the relation writer and the child own rules.</summary>
    protected Task<IActionResult> AddRelationChildAsync(
        IEntityRelationWriter writer, EntitySchema parentSchema, string parentId, string relationName, EntityDataModel data, CancellationToken cancellationToken) =>
        parentSchema.Relations.Any(r => r.Name.Equals(relationName, StringComparison.OrdinalIgnoreCase))
            ? GuardedAsync(async () =>
            {
                var created = await writer.AddAsync(parentSchema, parentId, relationName, data, cancellationToken).ConfigureAwait(false);
                return created is null ? (IActionResult)NotFound() : StatusCode(StatusCodes.Status201Created, created);
            })
            : Task.FromResult<IActionResult>(NotFound());

    /// <summary>Removes a child from one of the parent relations (204, or 404 when it is not a child).</summary>
    protected Task<IActionResult> RemoveRelationChildAsync(
        IEntityRelationWriter writer, EntitySchema parentSchema, string parentId, string relationName, string childId, CancellationToken cancellationToken) =>
        parentSchema.Relations.Any(r => r.Name.Equals(relationName, StringComparison.OrdinalIgnoreCase))
            ? GuardedAsync(async () =>
                await writer.RemoveAsync(parentSchema, parentId, relationName, childId, cancellationToken).ConfigureAwait(false) ? (IActionResult)NoContent() : NotFound())
            : Task.FromResult<IActionResult>(NotFound());

    /// <summary>
    /// Gives a form the lookup URL of each of its pointer fields (the choices for a select), so the client holds no URL
    /// templates. When a field points at the schema of the form itself (a page parent), the entity being edited is excluded
    /// from its own choices.
    /// </summary>
    public static EntityFormModel WithFieldLookups(EntityFormModel form, string? selfId = null, string lookupPath = "/api/lookups")
    {
        var lookups = form.Schema.Fields
            .Where(field => field.RefSchema is not null)
            .ToDictionary(
                field => field.Name,
                field => $"{lookupPath}?schema={Uri.EscapeDataString(field.RefSchema!)}"
                    + (!string.IsNullOrEmpty(selfId) && field.RefSchema == form.Schema.SchemaIri ? $"&exclude={Uri.EscapeDataString(selfId)}" : string.Empty),
                StringComparer.OrdinalIgnoreCase);
        return lookups.Count == 0 ? form : form with { Lookups = lookups };
    }

    /// <summary>The empty form for creating one (404 when this entity type is not created from the form), with its lookup links.</summary>
    protected async Task<ActionResult<EntityFormModel>> NewFormAsync(IEntityPresenter<TEntity> presenter, CancellationToken cancellationToken)
    {
        var form = await presenter.GetNewFormAsync(cancellationToken).ConfigureAwait(false);
        return form is null ? NotFound() : Ok(WithFieldLookups(form));
    }

    /// <summary>Deletes one entity (tombstones it): 204, 404 when it does not exist, 409 for a business rule.</summary>
    protected Task<IActionResult> DeleteEntityAsync(IEntityPresenter<TEntity> presenter, string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await presenter.DeleteAsync(id, cancellationToken).ConfigureAwait(false) ? (IActionResult)NoContent() : NotFound());

    /// <summary>Builds the grid links for <paramref name="path"/> (for example <c>/api/users</c>); relative to the site root, query string included.</summary>
    public static EntityGridLinks BuildGridLinks(EntityGridModel grid, string path)
    {
        var rowLinks = grid.Rows.ToDictionary(row => row.EntityId, row => $"{path.TrimEnd('/')}/{Uri.EscapeDataString(row.EntityId)}", StringComparer.Ordinal);
        var paging = grid.Paging;
        if (paging is null)
        {
            return new EntityGridLinks(path, grid.CanCreate ? path : null, Rows: rowLinks);
        }

        string Page(int number) =>
            $"{path}?page={number}&pageSize={paging.PageSize}"
            + (paging.Sort is null ? string.Empty : $"&sort={Uri.EscapeDataString(paging.Sort)}&descending={paging.Descending.ToString().ToLowerInvariant()}");

        return new EntityGridLinks(
            Page(paging.Page),
            grid.CanCreate ? path : null,
            paging.Page < paging.TotalPages ? Page(paging.Page + 1) : null,
            paging.Page > 1 ? Page(paging.Page - 1) : null,
            rowLinks);
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
