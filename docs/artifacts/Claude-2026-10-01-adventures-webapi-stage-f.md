# Stage review: `Adventures.WebApi` - the controller "low-hanging fruit" (stage F)

Date: 2026-10-01. Repo: `Adventures.Foundation`, branch `nguid-slice`. Not yet pushed.

## Why
After stage E promoted the Presenter, the question was whether to also force a generic MVC
controller (`EntityController<TEntity>`) into the library. Per your steer - open-generic
controllers fight Swagger/attribute routing, and controllers are cheap enough that copy/paste is a
fine outcome - the plan changed to sharing only the genuinely-duplicated *action bodies* as
protected helpers, not a forced inheritance hierarchy over the actual routed actions.

## What was built
- New project `Adventures.WebApi` - the first ASP.NET-Core-aware project in `Adventures.Foundation`
  (`<FrameworkReference Include="Microsoft.AspNetCore.App" />`, no Web SDK needed for a class
  library that only provides base classes).
- `ApiControllerBase` (non-generic): `GuardedAsync(Func<Task<IActionResult>>)` - the one thing
  actually duplicated verbatim today (`catch (InvalidOperationException ex) { return
  Conflict(ex.Message); }` appears in both `UsersController.Delete` and `ProfileController.DeleteMe`
  in `ai-research-blog`).
- `EntityControllerBase<TEntity> : ApiControllerBase`: protected (not `[Http*]`-decorated) helpers -
  `GetAsync`, `ListAsync`, `CreateAsync`, `UpdateAsync` - each taking the `IEntityPresenter<TEntity>`
  explicitly (no DI inside the base) and returning the right `ActionResult`/status code. Delete is
  deliberately **not** provided here: every real entity's delete needs at least an acting-user id
  for audit/authorization (see `IUserPresenter`'s guarded overload), which does not generalize the
  same way `Get`/`List`/`Create`/`Update` do - a concrete controller calls its own presenter's
  Delete directly, wrapped in `GuardedAsync`.
- A concrete controller built on this is now genuinely small - `TestEntityController` (the test
  double standing in for a real app's `UsersController`) is ~15 lines, each action a one-line
  delegation to the inherited helper.

## Verified
`EntityControllerBaseTests` (8 facts, against a real `IEntityPresenter<SchemaEntity>` backed by a
real `NQuadEntityRepository<SchemaEntity>` + `InMemoryNQuadStore` - not a fake, since this layer's
whole job is thin response-shaping over an already-tested presenter): Get/Create/List/Update happy
and not-found paths, plus `GuardedAsync` both passing through a normal result and mapping
`InvalidOperationException` to 409 Conflict. Full offline suite: **163 passing (was 155)**.

## Not covered / next
`ai-research-blog`'s `UsersController`/`ProfileController` still use the local, un-promoted
`Presenters/IUserPresenter.cs`/`UserPresenter.cs` from the earlier login/profile objective - the
next piece of this stage (in that repo) switches them to the promoted `IUserPresenter` and the new
`EntityControllerBase<User>`, and deletes the local `Presenters/` folder.
