# Claude review: keyed INQuadStore resolution and test reorganization (2026-09-26)

Reviewer: Claude. Owners of the reviewed work: Copilot (commit `2fa21d3`) and the human (commit `881a5ae`, uncommitted VS edits committed as-is by Claude). **Status: reviewed and accepted; one flaky-test hardening applied under rule 14; recommendations open for the owners.**

## Reviewed
`KeyedServiceResolutionExtensions.TryGetKeyedService`, `NQuadStoreKeyedServiceTests`, `NQuadStoreTests` (Func dispatcher variant), `NQuadStoreTestSupport` (`RunContractChecksAsync`, `PurgeAndReseedAsync`), the move of the contract tests into `NQuadStoreContract/`, the slnx now listing the NQuad and Entities projects, and the added `Microsoft.Extensions.DependencyInjection` package reference.

## What is good
- The keyed-singleton pattern with one `TryGetKeyedService` call for any dynamic key is a clean way to let the BLL stay ignorant of which stores exist. The `KeyedSingleton_ResolvesStore_ForAnyDynamicKey_WithoutConditionalLogic` theorem test states that intent well.
- Driving the shared contract against the store resolved from DI (`RunContractChecksAsync`) reuses the 12 contract facts instead of duplicating them, and `PurgeAndReseedAsync` restores the "purge and reseed the real database from the official seed file" workflow as a test.
- The slnx fix (NQuad, Entities and their tests were missing) closes a real gap. Build and all NQuad tests pass.

## Applied under rule 14 (real failure)
**Flaky Postgres tests.** In the first five runs after `2fa21d3`, `NQuadStoreTests.KeyedFactory_ResolvesPostgresStore_ForPostgresKey` failed three times; in 14 later runs it never failed. The failure message was not captured, so the cause is not proven. Two candidates: (a) `NQuadStoreToolTests` also purges and reseeds the real `n_quads` table but was not in the shared xUnit collection, so xUnit could run it in parallel with the two collection members; (b) another process (a second test run during the pairing session) hit the same shared table. (a) is a structural race regardless, so Claude added `[Collection(NQuadStorePostgresCollection.Name)]` to `NQuadStoreToolTests` in a separate commit. Result: 37 of 37 passing on three consecutive runs. If it recurs, capture the failure message first.

## Recommendations (owners decide; not applied)
1. **Hand-maintained `Checks` list** in `NQuadStoreTestSupport.ProvidedStoreContractTests` names the 12 contract facts explicitly. A 13th fact added to `NQuadStoreContractTests` would silently not run on the DI path. Either discover `[Fact]` methods by reflection, or add one test asserting the list count equals the number of `[Fact]` methods.
2. **`TryGetKeyedService` catches only `InvalidOperationException`**, but its documentation says it collapses "registered but construction failed" into a negative result. A factory that throws another type (for example `ArgumentException` or a provider exception) escapes. Conversely, `InvalidOperationException` also covers scope-validation and circular-dependency errors, which would now look like "no service for this key" and hide a real wiring bug. Consider catching a narrower, deliberate signal (a dedicated exception type thrown by factories), and logging at Warning when it swallows, so a misconfigured production key is not silent.
3. **Silent early return** in the opt-in Postgres facts reports green when nothing ran. Fine for CI opt-in, but consider surfacing "skipped" (for example the xUnit v3 dynamic skip) so a green run cannot be mistaken for a database-verified run.
4. **Tautological assertion** in the dynamic-key theorem: `Assert.Equal(resolved, store is not null)` is true by construction. Assert that "memory" resolves (`Assert.True(resolved)`) and leave "postgres" conditional.
5. The extension lives in the `Microsoft.Extensions.DependencyInjection` namespace for discoverability. That is a deliberate trade-off; note it in case a future framework adds a member with the same name.

## Open
Items 1 to 5 are for Copilot and the human. Claude will mark this review resolved when they are addressed or declined.
