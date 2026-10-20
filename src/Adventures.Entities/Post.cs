namespace Adventures.Entities;

/// <summary>See <see cref="EntityConstants.Content"/>; its schema ships in Sql/seed/seed-006-content.nq.</summary>
public sealed class Post(EntitySchema schema) : DynamicEntity(schema);
