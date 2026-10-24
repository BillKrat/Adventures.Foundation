// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>See <see cref="EntityConstants.Identity"/>; its schema ships in Sql/seed/seed-004-identity.nq.</summary>
public sealed class Tenant(EntitySchema schema) : DynamicEntity(schema);
