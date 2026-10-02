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


