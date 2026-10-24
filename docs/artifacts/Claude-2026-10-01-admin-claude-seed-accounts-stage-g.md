# Stage review: Admin/Claude seed accounts (stage G)

Date: 2026-10-01. Repo: `Adventures.Foundation`, branch `nguid-slice`. Not yet pushed.

## Why
Renaming the seeded "Bill Kratochvil" to "Admin User" (matching BlogEngine.NET's well-known
Admin/Admin convention) and adding a real "Claude" account for testing/dev - decided in
conversation, now that the Presenter/Controller promotion (stages E/F) gives any new app the same
CRUDL for free.

## Scope check, confirmed before touching anything
The graph GUID (`b7963bd0-...`) is just an opaque partition key shared by every seed row (roles,
tenants, blogs, schemas) - never exposed as "Bill" anywhere. Only the literal quad *values* on the
user subject itself needed to change; `EntityConstants.User.DefaultGraph`, `NQuadStoreToolTests`'s
subject query, and every GUID-keyed reference elsewhere in the file were untouched.

## What changed
- `seed.nq`: the existing user subject's `givenName`/`familyName`/`accountName`/`displayName`
  quads now read "Admin"/"User"/"Admin"/"Admin User"; `mbox` is now the generic
  `admin@global-webnet.com`. The real `phone`/`birthday` quads (806-340-1044 and an actual
  birthdate) are removed entirely, not just relabeled - that was real personal data sitting in a
  seed file regardless of whose name was attached to it.
- A second user subject added (fresh GUID) for "Claude" - UserName/DisplayName "Claude",
  `claude@global-webnet.com`, no phone/DOB. Same graph as everything else in the file.
- Tests updated: `UserAndUserSchemaTests`, `UserBllTests`, `UserPresenterTests`,
  `NQuadStoreToolTests` - renamed fixture values, dropped the now-nonexistent phone assertion, and
  each gained one new fact confirming the seed file's "Claude" row is actually there and
  independently resolvable (not just asserting Admin still works).
- `poc` deliberately left untouched - it is frozen (2026-09-26 decision); its own `AGENTS.md` now
  records that its seed-triad copy is a stale snapshot, not kept in sync with this one going
  forward.

## Verified
Full offline suite: **165 passing (was 163)**. Confirmed the one test that actually exercises a
real store round-trip against this exact seed file (`NQuadStoreToolTests.PurgeReseedAndQuery_RoundTrips`,
~6s, not skipped) passes against the renamed data, not just the in-memory-only tests.

## Not covered / next
`ai-research-blog`'s own test fixtures (`UsersControllerTests.cs`, `ProfileControllerTests.cs`,
`entity-form.spec.ts`) still use "BillKrat" as arbitrary fixture strings - not real seed data, so
not functionally required, but worth a small consistency pass since `UsersControllerTests.cs`
already has a "Claude" fixture alongside "Bill". Stage H (load `SchemaEntity`/`SchemaFieldEntity`'s
`MetaSchema` from seed data) and stage I (real Postgres credentials for Admin/Claude) are next.
