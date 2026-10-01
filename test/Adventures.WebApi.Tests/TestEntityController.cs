using Adventures.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.WebApi.Tests;

/// <summary>
/// A minimal, concrete controller exercising <see cref="EntityControllerBase{TEntity}"/>'s
/// protected helpers and <see cref="ApiControllerBase.GuardedAsync"/> - standing in for a real
/// app's small, attribute-decorated controller (e.g. ai-research-blog's UsersController).
/// </summary>
public sealed class TestEntityController(IEntityPresenter<SchemaEntity> presenter) : EntityControllerBase<SchemaEntity>
{
    public Task<ActionResult<EntityFormModel>> Get(string id, CancellationToken cancellationToken) =>
        GetAsync(presenter, id, cancellationToken);

    public Task<ActionResult<IReadOnlyList<EntityDataModel>>> List(CancellationToken cancellationToken) =>
        ListAsync(presenter, cancellationToken);

    public Task<ActionResult<EntityFormModel>> Create(EntityDataModel data, CancellationToken cancellationToken) =>
        CreateAsync(presenter, data, cancellationToken);

    public Task<ActionResult<EntityFormModel>> Update(string id, EntityDataModel data, CancellationToken cancellationToken) =>
        UpdateAsync(presenter, id, data, cancellationToken);

    /// <summary>Exercises GuardedAsync directly - delegate throws to prove the Conflict mapping, without needing a real guarded presenter.</summary>
    public Task<IActionResult> DeleteGuarded(Func<Task<IActionResult>> action) => GuardedAsync(action);
}
