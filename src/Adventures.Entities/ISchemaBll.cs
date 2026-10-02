using Adventures.Common.Interfaces;

namespace Adventures.Entities;

/// <summary>
/// The business-rules layer for <see cref="SchemaEntity"/>/<see cref="SchemaFieldEntity"/> -
/// storage-agnostic, depends only on <see cref="IEntityRepository{TEntity}"/> (the Dal; no separate
/// wrapper class needed, since <c>NQuadEntityRepository{SchemaEntity}</c> already is the Dal). Kept
/// separate from its implementation (<see cref="SchemaBll"/>) so a versioned or alternate
/// implementation can be swapped in via DI without touching callers - the same pattern
/// <see cref="IUserBll"/> follows.
/// </summary>
public interface ISchemaBll : IBll
{
    /// <summary>
    /// Fetches <paramref name="schemaEntityId"/> and every <see cref="SchemaFieldEntity"/> its
    /// "Field" values reference, then builds the <see cref="EntitySchema"/> other entities need at
    /// construction time. Only works for a schema created through the new, generic-repository
    /// convention - a legacy, hand-seeded schema (still read via <c>Adventures.Data.NQuad.SchemaDal</c>)
    /// has no <see cref="SchemaFieldEntity"/> instances under this convention's base IRI to fetch.
    /// </summary>
    Task<EntitySchema> LoadEntitySchemaAsync(string schemaEntityId, CancellationToken cancellationToken = default);
}
