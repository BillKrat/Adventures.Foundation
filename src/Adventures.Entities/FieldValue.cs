// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// A single stored value for a dynamic entity field, paired with the id of the record
/// that produced it (e.g. the underlying quad/row id). Update and delete operations
/// against the originating store need this id - the raw value alone (e.g. "Bill") is
/// not enough to target a specific record for mutation.
/// </summary>
public sealed record FieldValue(IEntityId Id, object Value)
{
	public static FieldValue Of(string id, object value) => new(new EntityId(id), value);
}


