# Stage review: `IEntityPresenter<TEntity>`/`EntityPresenter<TEntity>` (stage E)

Date: 2026-10-01. Repo: `Adventures.Foundation`, branch `nguid-slice`. Not yet pushed.

## Why
`UserPresenter`/`IUserPresenter` (built in `ai-research-blog`'s WebApi for the login/profile
objective) turned out to have nothing User-specific in most of its methods - every bit except the
UserName lookup and the delete guard was a pure pass-through over `IEntityRepository<TEntity>` +
`EntityFormModel`. Per your call ("the thing worth putting in is a generic... agreed"), this stage
promotes that into `Adventures.Foundation` so any new app gets CRUDL-over-a-schema-driven-entity
for free, the same way `NQuadEntityRepository<TEntity>` already serves `User`/`SchemaEntity`/
`SchemaFieldEntity` with zero entity-specific code.

## What was built
- `IEntityPresenter<TEntity> : IPresenter` / `EntityPresenter<TEntity>` (`Adventures.Entities`):
  `GetFormAsync(id)`, `ListAsync()` -> `IReadOnlyList<EntityDataModel>` (no separate "summary" DTO -
  a list UI projects whatever fields it wants client-side), `CreateAsync`/`UpdateAsync`
  (`EntityDataModel` in, `EntityFormModel` out), `DeleteAsync(id)` (ungated). Depends directly on
  `IEntityRepository<TEntity>` + `EntitySchema` + an entity factory - the same constructor shape
  `NQuadEntityRepository<TEntity>` itself uses. No Bll in the generic base: every method here is a
  pass-through, so there is nothing for a generic Bll to add.
- `IUserPresenter : IEntityPresenter<User>` / `UserPresenter : EntityPresenter<User>` moved into
  `Adventures.Entities`, trimmed to exactly the two things that are not generic:
  `GetFormByUserNameAsync` and a guarded `DeleteAsync(id, currentUserId)` - a same-named overload of
  the base's ungated one (different arity, no `new`/override conflict). `UserPresenter` still takes
  `IUserBll` (for those two), alongside `IEntityRepository<User>` (for the inherited base methods).
- **Deliberately kept concrete and named**, not collapsed to bare `EntityPresenter<User>` usage:
  `AddLifetimeServices()`'s reflection scan needs a concrete, non-generic type to auto-register
  (keyed by class name, unkeyed when unambiguous) - the same mechanism `MockBll`/`MockBllV2` already
  prove. A future `UserPresenterV2 : EntityPresenter<User>, IUserPresenter` gets
  `ResolveKey<IUserPresenter>("UserPresenterV2")` for free - the versioned-Bll concept extends to
  Presenters with zero extra plumbing, by design.

## Verified
Test-first: `EntityPresenterTests` (6 facts, against a real `NQuadEntityRepository<SchemaEntity>` +
`InMemoryNQuadStore` - proves the generic base needs zero entity-specific code, using `SchemaEntity`
precisely because it has no business rules, so there's no "SchemaPresenter" subclass needed either)
and `UserPresenterTests` (4 facts, covering only the two additions). Full offline suite:
**155 passing (was 145)**.

## Not covered / next (per the agreed plan)
- `ai-research-blog` still has its own local `Presenters/IUserPresenter.cs`/`UserPresenter.cs`
  (unchanged) - stage F switches it to the promoted version and deletes the local copy, alongside
  building the controller-layer "low-hanging fruit" (`Adventures.WebApi`, `ApiControllerBase`,
  `EntityControllerBase<TEntity>`).
- No generic MVC controller - deliberately: per your steer, open-generic controllers fight
  Swagger/routing, so the controller layer shares action *bodies* as protected helpers, not a
  forced inheritance hierarchy. That's stage F.
