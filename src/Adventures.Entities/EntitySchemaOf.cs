namespace Adventures.Entities;

/// <summary>
/// The <see cref="EntitySchema"/> of one entity type, typed so dependency injection can hold one per entity. A bare
/// <see cref="EntitySchema"/> registration can only ever mean one schema, which stops a second presenter existing.
/// Registered by the host's repository registration (<c>AddNQuadEntityRepository</c>).
/// </summary>
public sealed record EntitySchemaOf<TEntity>(EntitySchema Schema)
    where TEntity : DynamicEntity;
