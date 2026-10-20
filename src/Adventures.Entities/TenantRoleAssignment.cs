namespace Adventures.Entities;

/// <summary>See <see cref="EntityConstants.Identity"/>; its schema ships in Sql/seed/seed-004-identity.nq.</summary>
public sealed class TenantRoleAssignment(EntitySchema schema) : DynamicEntity(schema);
