# Claude review: keyed INQuadStore resolution and test reorganization (2026-09-26)

Reviewer: Claude. Owners of the reviewed work: Copilot (commit `2fa21d3`) and the human (commit `881a5ae`, uncommitted VS edits committed as-is by Claude). **Status: RESOLVED 2026-09-26.** The human asked Claude to fix or drop the recommendations rather than carry them.

## Reviewed
`KeyedServiceResolutionExtensions.TryGetKeyedService`, `NQuadStoreKeyedServiceTests`, `NQuadStoreTests` (Func dispatcher variant), `NQuadStoreTestSupport` (`RunContractChecksAsync`, `PurgeAndReseedAsync`), the move of the contract tests into `NQuadStoreContract/`, the slnx now listing the NQuad and Entities projects, and the added `Microsoft.Extensions.DependencyInjection` package reference.

## What is good
- The keyed-singleton pattern with one `TryGetKeyedService` call for any dynamic key is a clean way to let the BLL stay ignorant of which stores exist. The `KeyedSingleton_ResolvesStore_ForAnyDynamicKey_WithoutConditionalLogic` theorem test states that intent well.
- Driving the shared contract against the store resolved from DI (`RunContractChecksAsync`) reuses the 12 contract facts instead of duplicating them, and `PurgeAndReseedAsync` restores the "purge and reseed the real database from the official seed file" workflow as a test.
- The slnx fix (NQuad, Entities and their tests were missing) closes a real gap. Build and all NQuad tests pass.

## Applied under rule 14 (real failure)
**Flaky Postgres tests.** In the first five runs after `2fa21d3`, `NQuadStoreTests.KeyedFactory_ResolvesPostgresStore_ForPostgresKey` failed three times; in 14 later runs it never failed. The failure message was not captured, so the cause is not proven. Two candidates: (a) `NQuadStoreToolTests` also purges and reseeds the real `n_quads` table but was not in the shared xUnit collection, so xUnit could run it in parallel with the two collection members; (b) another process (a second test run during the pairing session) hit the same shared table. (a) is a structural race regardless, so Claude added `[Collection(NQuadStorePostgresCollection.Name)]` to `NQuadStoreToolTests` in a separate commit. Result: 37 of 37 passing on three consecutive runs. If it recurs, capture the failure message first.

## Dispositions of the recommendations (Claude, 2026-09-26, at the human's direction)
1. Hand-maintained `Checks` list: **fixed.** Facts are now discovered by reflection (delegates, so failures surface unwrapped), and the run asserts the list is not empty.
2. `TryGetKeyedService` catching only `InvalidOperationException`, and swallowing without logging: **declined.** A hardening idea, not a failure; revisit if a misconfigured key ever bites.
3. Opt-in Postgres facts return early and report green: **declined.** Existing, intentional convention for opt-in integration tests.
4. Tautological assertion in the dynamic-key theorem: **fixed.** "memory" now asserts it resolves.
5. Extension in the `Microsoft.Extensions.DependencyInjection` namespace: **declined.** Deliberate trade-off, no action.

Verified: 37 of 37 pass on consecutive runs after the changes.