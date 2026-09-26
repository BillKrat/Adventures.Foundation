# Plan: in-memory N-Quad store behind a shared interface

Written 2026-09-25 (no code yet). Goal: pull the POC's in-memory store into `Adventures.Data.NQuad`, put an interface on it that matches `NpgsqlNQuadStore` (async, `CancellationToken`), and make both implement it, so development and tests can run without touching PostgreSQL. At seed/deploy time we swap the implementation.

Sources reviewed: `src/Adventures.Data.NQuad/NpgsqlNQuadStore.cs` and, in the `poc` repo (read-only reference), `nquad-end-to-end-poc/src/Poc/InMemoryQuadrupleStore.cs`.

## What exists

| | `NpgsqlNQuadStore` (Foundation) | `InMemoryQuadrupleStore` (poc) |
|---|---|---|
| Row type | `NQuad(Id, Subject, Predicate, Object, Graph?)` | `Quadruple(Id, Subject, Predicate, Object, Graph?)`, identical shape |
| Style | async, `CancellationToken` on every method | synchronous, no tokens |
| Write | `InsertAsync`, `InsertManyAsync`, `SeedFromFileAsync`, `PurgeAsync` (TRUNCATE), `CreateTableAsync` | `Create` (throws `InvalidOperationException` on duplicate id), `Update(id, replacement)` (throws `KeyNotFoundException` / `ArgumentException`), `Delete(id)` returns bool |
| Read | `QueryAsync(subject?, predicate?, object?, graph?)` (null = wildcard), `CountAsync` | `Read(id)` returns null if absent, `List()` |
| Storage | table `n_quads`, name hard-coded | `Dictionary<Guid, Quadruple>`, not thread-safe |

The two surfaces only partly overlap. Postgres has query-by-terms, count, bulk insert, purge, and seed-from-file but no read/update/delete by id. In-memory has read/update/delete by id but no term query, count, or bulk. The POC's consumers (`DalBase`, `EntitySchema`, `NQuadParser`) call `Create`/`Delete`/`List` and then filter with LINQ, which is a full scan (fine in memory).

## Suggested shape (to confirm tomorrow)

- **Names:** `INQuadStore` and `InMemoryNQuadStore` in `Adventures.Data.NQuad`; the record stays `NQuad`. The POC keeps its own copy of `Quadruple`/`InMemoryQuadrupleStore` untouched (reference-only repo).
- **Interface = the Postgres surface that both can honour:** `InsertAsync`, `InsertManyAsync`, `QueryAsync`, `CountAsync`, `PurgeAsync`, all with `CancellationToken`.
- **`SeedFromFileAsync` need not be per-store:** it is "parse with `NQuadFileParser`, then `InsertManyAsync`", so it can be an extension method over the interface and work for any store.
- **`CreateTableAsync` is Postgres-specific:** keep it off `INQuadStore` (a no-op on in-memory adds nothing). Candidate: a separate small initializer interface, or leave it on the concrete type.
- **By-id operations** (`GetAsync`, `UpdateAsync`, `DeleteAsync`): the POC has them, and the deferred update/delete work on `FieldValue.Id` will need them. Adding them means writing the Postgres SQL (test-first). Decision needed: now, or as the next stage.

## Behaviours the contract must pin down

1. **Duplicate id on insert.** In-memory throws `InvalidOperationException`; Postgres would raise a provider primary-key violation. Pick one documented behaviour (for example, wrap so both throw the same exception type).
2. **Result ordering.** Postgres `QueryAsync` has no `ORDER BY`; a dictionary enumerates roughly in insertion order. Tests must not depend on order, or the query gets a defined order.
3. **Null wildcard.** Any null term matches everything; the in-memory version must match exactly.
4. **Null graph** represents the default graph (the POC has a test for this).
5. **Empty batch** returns 0 without touching storage (Postgres already does this).
6. **Cancellation.** In-memory should honour an already-cancelled token, so behaviour matches.
7. **Thread safety.** Use a lock or a concurrent dictionary; the in-memory store will back tests and possibly the API.
8. **Return counts.** `PurgeAsync` returns whatever `ExecuteAsync` yields for TRUNCATE (possibly -1). Define what it means before the contract asserts on it.

## Test approach

- **One abstract contract test class** exercised against both implementations. In-memory always runs; Postgres runs only when `ConnectionStrings:Postgres` is present, as today.
- **Seed the contract from the POC's 8 tests** in `poc/nquad-end-to-end-poc/tests/Poc.Tests/QuadrupleStoreTests.cs` (create/read, duplicate id, unknown id, update keeps id, mismatched id rejected, delete only the target, multiple domains/blogs, null graph), translated to `NQuad` and async, plus term-query, count, purge, and bulk insert.
- **Safety flag:** the existing Postgres tests already `TRUNCATE` the dev `n_quads` table. Running the contract against Postgres would do the same. Consider making the table name configurable so contract runs use a scratch table, or keep the Postgres contract run behind an explicit opt-in.
- Keep growth red-green and small: interface plus in-memory first, then re-point `NpgsqlNQuadStore` at the interface.

## Swapping implementations

Register through DI (for example an `AddNQuadStore` extension) with a configuration switch such as `NQuad:Store = InMemory | Postgres`, so tests and dev default to in-memory and deploy uses Postgres.

## Decisions (answered 2026-09-26) and status
1. **By-id ops:** not in this layer. Two concerns: store maintenance (`INQuadStore`, this plan) and CRUDL over `DynamicEntity`-derived classes, User first (next stage, its own interface).
2. **Duplicate id:** the store throws; the type is not pinned. Exception handling is a separate design: [Claude-decision-2026-09-exception-handling-direction.md](Claude-decision-2026-09-exception-handling-direction.md).
3. **Scratch table:** yes; `NpgsqlNQuadStore(connectionString, tableName = "n_quads")`.
4. **`CreateTableAsync`:** behind `INQuadStoreInitializer`.
5. **Consumers this stage:** only the two stores.

**Stage 1 is built and green** (interface, initializer, in-memory store, shared contract run against both stores): see [artifacts/Claude-2026-09-26-nquad-store-stage-1.md](artifacts/Claude-2026-09-26-nquad-store-stage-1.md). Still to do from this plan: the DI switch (`NQuad:Store`).