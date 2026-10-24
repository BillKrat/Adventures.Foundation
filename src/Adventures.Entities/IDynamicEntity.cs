// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

public interface IDynamicEntity
{
    IEntityId? EntityId { get; }

    IReadOnlyList<DynamicFieldState> Fields { get; }

    IReadOnlyCollection<string> DirtyFieldIds { get; }

    /// <summary>Returns the first stored value for <paramref name="propertyName"/>, or null if none.</summary>
    object? GetValue(string propertyName);

    /// <summary>Returns every stored value for <paramref name="propertyName"/>.</summary>
    IReadOnlyList<object> GetValues(string propertyName);

    /// <summary>
    /// Returns the first stored (id, value) pair for <paramref name="propertyName"/>, or null if none.
    /// The id is required by callers that need to update or delete the underlying record -
    /// the raw value alone (e.g. "Bill") does not identify which record to mutate.
    /// </summary>
    FieldValue? GetFieldValue(string propertyName);

    /// <summary>Returns every stored (id, value) pair for <paramref name="propertyName"/>.</summary>
    IReadOnlyList<FieldValue> GetFieldValues(string propertyName);
}
