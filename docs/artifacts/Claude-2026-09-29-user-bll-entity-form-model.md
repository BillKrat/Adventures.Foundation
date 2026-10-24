# Stage review: `UserBll` + `EntityFormModel` + `IPresenter` marker (library prep for profile CRUDL)

Date: 2026-09-29. Repo: `Adventures.Foundation`, branch `nguid-slice`. Not yet pushed.

## Why
First step of the login/profile objective: give `ai-research-blog`'s WebApi something to build a
schema-driven profile page on, without any web-app code yet. Two library pieces, both storage-
agnostic like `SchemaBll` before them.

## What was built
- `EntityFormModel` (`src/Adventures.Entities/EntityFormModel.cs`): the agreed "standard object" -
  `EntitySchemaModel` (SchemaIri + `EntityFieldSchema[]`: Name/Type/IsRequired, no values) and
  `EntityDataModel` (EntityId + a Name->value dictionary), bundled but kept separate, per your
  direction that the client works with Schema and Entity as two named parts, not a merged per-field
  list and not a DataSet. `EntityFormModel.From(EntitySchema, DynamicEntity)` builds one - generic,
  works for any entity type, not just `User`. Deliberately not `EntitySchema`/`DynamicEntity`
  themselves: a `DynamicEntity` is a `DynamicObject` and doesn't serialize cleanly, and both are
  domain/DAL-shaped, not something a UI form control should touch directly.
- `UserBll`/`IUserBll` (`src/Adventures.Entities/UserBll.cs`): storage-agnostic, depends only on
  `IEntityRepository<User>` (the Dal - no wrapper class, same reasoning `SchemaBll` established).
  `FindByUserNameAsync` (case-insensitive first-match, no uniqueness enforcement - matches "no
  validation yet"), `CreateAsync`/`GetAsync`/`ListAsync`/`UpdateAsync` (pass through), and
  `DeleteAsync(userId, currentUserId)` - the one business rule: throws if they're equal.
- `IPresenter` (`Adventures.Common/Interfaces/IPresenter.cs`): mirrors `IBll`/`IDal` exactly
  (`: IScopedLifetime`, no members). The WebApi-side `UserPresenter` (stage C) will implement it so
  it's auto-registered and resolved the same way `MockBll`/`MockDal` are in `MvpVmTests.cs`.
- **First dependency for `Adventures.Entities`**: adding `IUserBll : IBll` required a project
  reference `Adventures.Entities -> Adventures.Common -> Adventures.Ioc`. Previously
  `Adventures.Entities.csproj` had none. Flagged in the plan before doing it; still storage-agnostic,
  just no longer dependency-free.

## Verified
Test-first: `EntityFormModelTests` (2 facts, pure - no store) and `UserBllTests` (5 facts, against a
real `NQuadEntityRepository<User>` + `InMemoryNQuadStore` seeded from `seed.nq`, mirroring
`SchemaEntityRepositoryTests.cs`'s pattern) written before the implementation existed. One bug
caught immediately in my own test, not the implementation: the test's Widget schema declared its
`Id` field as `"Guid"` but set a non-Guid string value - `DynamicEntity.Set`'s type conversion threw
until the test schema was fixed to `"String"`. Full offline suite: **143 passing (was 136)**.

## Not covered / next
Stage C (`ai-research-blog`): DI wiring (`AddLifetimeServices`, seeded `InMemoryNQuadStore`,
`IEntityRepository<User>` registration), `IUserPresenter`/`UserPresenter`, `UsersController` (full
CRUDL), `ProfileController` rewritten onto the new stack. This is the larger, cross-repo stage -
pausing here for review first.
