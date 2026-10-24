// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.WebApi;

/// <summary>
/// The six actions every admin entity collection has on the form and grid contracts - the sorted and paged list, the empty
/// form, one entity, create, update and delete - so a new entity type is a small derived controller that names its route and
/// its authorisation and passes its presenter, nothing more. Every form it returns carries its lookup links; a derived controller
/// adds what is its own (actions such as publish) by overriding <see cref="Decorate"/>.
/// </summary>
public abstract class AdminEntityControllerBase<TEntity>(IEntityPresenter<TEntity> presenter) : EntityControllerBase<TEntity>
    where TEntity : DynamicEntity
{
    /// <summary>The URL of this collection (the request path without a trailing entity id or "new").</summary>
    protected string CollectionPath(string? id = null)
    {
        var path = Request.Path.Value!.TrimEnd('/');
        foreach (var tail in new[] { id, "new" }.Where(t => !string.IsNullOrEmpty(t)))
        {
            if (path.EndsWith("/" + tail, StringComparison.OrdinalIgnoreCase))
            {
                return path[..^(tail!.Length + 1)];
            }
        }

        return path;
    }

    /// <summary>Adds the server-supplied links (and, in a derived controller, actions) to a form before it is returned.</summary>
    protected virtual EntityFormModel Decorate(EntityFormModel form, string collectionPath) =>
        WithFieldLookups(form, form.Entity.EntityId);

    /// <summary>
    /// The asynchronous form of <see cref="Decorate"/>, for a derived controller that needs something it must await (which plug-ins are on)
    /// to decorate a form; by default it is the synchronous one.
    /// </summary>
    protected virtual Task<EntityFormModel> DecorateAsync(EntityFormModel form, string collectionPath, CancellationToken cancellationToken) =>
        Task.FromResult(Decorate(form, collectionPath));

    /// <summary>A derived controller adds what is its own to a grid (the filters it offers) before it is returned.</summary>
    protected virtual EntityGridModel DecorateGrid(EntityGridModel grid) => grid;

    [HttpGet]
    public async Task<ActionResult<EntityGridModel>> List([FromQuery] EntityGridQuery query, CancellationToken cancellationToken)
    {
        var result = await GridAsync(presenter, query, cancellationToken);
        return result.Result is OkObjectResult { Value: EntityGridModel grid } ok ? Ok(DecorateGrid(grid)) : result;
    }

    [HttpGet("new")]
    public async Task<ActionResult<EntityFormModel>> New(CancellationToken cancellationToken)
    {
        var form = await presenter.GetNewFormAsync(cancellationToken);
        return form is null ? NotFound() : Ok(await DecorateAsync(form, CollectionPath(), cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<EntityFormModel>> Get(string id, CancellationToken cancellationToken)
    {
        var form = await presenter.GetFormAsync(id, cancellationToken);
        return form is null ? NotFound() : Ok(await DecorateAsync(form, CollectionPath(id), cancellationToken));
    }

    /// <summary>A derived controller may fill in what only the host knows (the signed-in author) before the entity is created.</summary>
    protected virtual Task<EntityDataModel> PrepareForCreateAsync(EntityDataModel request, CancellationToken cancellationToken) =>
        Task.FromResult(request);

    [HttpPost]
    public async Task<ActionResult<EntityFormModel>> Create([FromBody] EntityDataModel request, CancellationToken cancellationToken)
    {
        request = await PrepareForCreateAsync(request, cancellationToken);
        var result = await CreateAsync(presenter, request, cancellationToken);
        return result.Result is ObjectResult { Value: EntityFormModel created } created201
            ? StatusCode(created201.StatusCode ?? 201, await DecorateAsync(created, CollectionPath(), cancellationToken))
            : result;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<EntityFormModel>> Update(string id, [FromBody] EntityDataModel request, CancellationToken cancellationToken)
    {
        var result = await UpdateAsync(presenter, id, request, cancellationToken);
        return result.Result is OkObjectResult { Value: EntityFormModel updated } ? Ok(await DecorateAsync(updated, CollectionPath(id), cancellationToken)) : result;
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken cancellationToken) =>
        DeleteEntityAsync(presenter, id, cancellationToken);
}
