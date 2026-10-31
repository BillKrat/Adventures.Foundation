// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// Identifies a single DynamicEntity instance (the "Id" schema field). Kept as its own
/// type rather than a raw string so callers cannot accidentally pass an arbitrary field
/// value where an entity identity is required.
/// </summary>
public interface IEntityId
{
	string Value { get; }
}

public sealed record EntityId(string Value) : IEntityId;

