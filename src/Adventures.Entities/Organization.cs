namespace Adventures.Entities;

/// <summary>See <see cref="EntityConstants.Identity"/>; its schema ships in Sql/seed/seed-004-identity.nq.</summary>
public sealed class Organization(EntitySchema schema) : DynamicEntity(schema);
