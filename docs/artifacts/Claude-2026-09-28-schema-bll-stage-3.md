# Stage review: `SchemaBll` (stage 3 of 3)

Date: 2026-09-28. Repo: `Adventures.Foundation`, branch `nguid-slice`. Not yet pushed.

## Why
Stages 1-2 split `EntitySchema` into a plain metadata Entity, an N-Quad-specific Dal
(`SchemaDal`), and proved `SchemaEntity`/`SchemaFieldEntity` are ordinary `DynamicEntity` types
with zero repository changes. What was still missing: the business-rules layer you asked for -
validating a schema's fields before trusting them, and turning persisted `SchemaEntity`/
`SchemaFieldEntity` instances into the `EntitySchema` metadata other entities need.

## A scope correction, made before writing any code
The original stage-3 plan said `SchemaDal` would be "upgraded to fetch via
`NQuadEntityRepository<SchemaEntity>`/`<SchemaFieldEntity>` instead of hand-walking triples,
retiring stage 1's interim triple-walking." Working through it surfaced why that can't happen yet:
the legacy `schema/User`/`schema/AuditRecord` field subjects live at `schema/{SchemaName}/{Field}`
(e.g. `schema/User/Id`), but stage 2's `SchemaFieldEntity` repository convention uses a flat,
separate `schema-field/{id}` base IRI. A single `NQuadEntityRepository<TEntity>` only ever serves
one flat base IRI, so it structurally cannot read the legacy, nested field subjects. This is the
same "is a Schema" stopgap you flagged after stage 2 - not something to paper over with a special
case, so it's deferred to the seed-maturity pass you described. Stage 1's `SchemaDal.Load(quads,
schemaIri)` is therefore **unchanged and still the only way to read the legacy User/AuditRecord
schemas** - the two original call sites (`NQuadEntityRepositoryTests.cs`, `UserAndUserSchemaTests.cs`)
were deliberately left alone this stage.

## What was built
- `SchemaBll` (new, `Adventures.Entities` - storage-agnostic, depends only on
  `IEntityRepository<TEntity>`):
  - `ToEntitySchema(SchemaEntity, IReadOnlyList<SchemaFieldEntity>)` - the synchronous
    translation. Checks each field actually carries Name/FieldType/Predicate (something
    `EntitySchema.Create` can't check, since it only ever sees already-built
    `EntitySchemaField` records), then delegates to `EntitySchema.Create` for the structural
    invariants (≥1 field, no duplicate name/predicate) already proven in stage 1.
  - `LoadEntitySchemaAsync(schemaEntityId)` - fetches a `SchemaEntity` and every
    `SchemaFieldEntity` its `Field` values reference (through the two repositories injected via
    constructor), then calls `ToEntitySchema`.
- **No new Dal class for the new convention.** `NQuadEntityRepository<SchemaEntity>`/
  `<SchemaFieldEntity>` (stage 2, unchanged) already *are* the Dal - wrapping them in another
  same-purpose class would have been a pass-through with no behavior, which is exactly the kind
  of layer this whole refactor was meant to remove. This satisfies "BLL and DAL... only in
  NQuadEntityRepository if it applies to DynamicEntity" directly: the Dal is the generic
  repository, unmodified; `SchemaBll` is the only new type.

## Verified
`ToEntitySchema` tested in `Adventures.Entities.Tests/SchemaBllTests.cs` (4 facts, no store: builds
fields from plain in-memory `SchemaFieldEntity` instances, throws on a field missing "Name",
throws on two fields mapped to the same predicate, throws on zero fields) - pure logic, tested
where it lives. `LoadEntitySchemaAsync` tested end-to-end against a real `InMemoryNQuadStore` in
`Adventures.Data.NQuad.Tests/SchemaEntityRepositoryTests.cs` (1 fact: two real `SchemaFieldEntity`
instances persisted, a `SchemaEntity` referencing them persisted, then `SchemaBll` loads and
reassembles the same `EntitySchema` a hand-built one would produce). Full offline suite:
**136 passing (was 131)**.

## Not covered / open items
- Reconciling the legacy hand-seeded schemas with the new generic-repository convention (a
  `seed.nq` migration) - deferred, per your call, to whenever the seed file gets a broader
  maturity pass. At that point `SchemaDal.Load(quads, schemaIri)` likely retires in favor of
  `SchemaBll.LoadEntitySchemaAsync` everywhere, including the two original User-schema call sites.
- No schema-authoring API/UI exists yet - `SchemaBll` is exercised only by tests so far.

```mermaid
classDiagram
    class SchemaBll {
        -IEntityRepository~SchemaEntity~ schemas
        -IEntityRepository~SchemaFieldEntity~ fields
        +LoadEntitySchemaAsync(schemaEntityId) EntitySchema
        +ToEntitySchema(schema, fields)$ EntitySchema
    }
    class EntitySchema {
        +Create(schemaIri, fields)$
    }
    class SchemaDal {
        <<Adventures.Data.NQuad, unchanged>>
        +Load(quads, schemaIri)$ EntitySchema
        note "still the only path for\nlegacy seed-authored schemas"
    }
    SchemaBll ..> EntitySchema : Create()
    SchemaBll ..> "NQuadEntityRepository~SchemaEntity~" : GetAsync (the Dal, unmodified)
    SchemaBll ..> "NQuadEntityRepository~SchemaFieldEntity~" : GetAsync (the Dal, unmodified)
```

## Summary of all three stages
`EntitySchema` no longer does Dal or Bll work - it's a plain metadata value object
(`EntitySchema.Create`). `SchemaEntity`/`SchemaFieldEntity` are ordinary `DynamicEntity` types,
CRUDL'd through the same, unmodified `NQuadEntityRepository<TEntity>` every other entity uses.
`SchemaBll` is the one new business-rules type, validating and assembling them. `NQuadEntityRepository`
itself had zero changes across all three stages. 116 -> 136 tests (20 new, all red-first).
