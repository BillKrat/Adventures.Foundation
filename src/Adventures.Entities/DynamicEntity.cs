using System.Dynamic;

namespace Adventures.Entities;

/// <summary>
/// Base class for a schema-driven dynamic entity (e.g. User). Values are stored internally as
/// <see cref="FieldValue"/> - an (id, value) pair - rather than bare objects. The id (typically
/// the underlying quad/row id from the originating store) is required for update/delete against
/// that store; storing only the raw value (e.g. "Bill") would make those operations impossible.
/// </summary>
public abstract class DynamicEntity : DynamicObject, IDynamicEntity
{
    private readonly EntitySchema _schema;
    private readonly Dictionary<string, List<FieldValue>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FieldValue>> _originalValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyFieldIds = new(StringComparer.Ordinal);

    protected DynamicEntity(EntitySchema schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public IEntityId? EntityId =>
        GetValue(EntityIdPropertyName) is { } value
            ? new EntityId(value.ToString()!)
            : null;

    /// <summary>
    /// The schema field name every entity's identifier is expected to use. Generic-engine code
    /// relies on this convention rather than a per-entity setting, so every schema needs a field
    /// named exactly this to be usable as an entity identity.
    /// </summary>
    public const string EntityIdPropertyName = "Id";

    public IReadOnlyCollection<string> DirtyFieldIds => _dirtyFieldIds.ToArray();

    public IReadOnlyList<DynamicFieldState> Fields =>
        _schema.Fields.Values
            .Select(schemaField => new DynamicFieldState(
                schemaField.EntityType,
                schemaField.Name,
                schemaField.Id,
                schemaField.Type,
                schemaField.IsRequired,
                GetFieldValuesFrom(_originalValues, schemaField.Name).FirstOrDefault(),
                GetFieldValues(schemaField.Name).FirstOrDefault()))
            .ToArray();

    public object? GetValue(string propertyName) =>
        GetFieldValue(propertyName)?.Value;

    public IReadOnlyList<object> GetValues(string propertyName) =>
        GetFieldValues(propertyName).Select(fv => fv.Value).ToArray();

    public FieldValue? GetFieldValue(string propertyName) =>
        GetFieldValues(propertyName).FirstOrDefault();

    public IReadOnlyList<FieldValue> GetFieldValues(string propertyName) =>
        _values.TryGetValue(propertyName, out var values)
            ? values.ToArray()
            : [];

    internal IEnumerable<KeyValuePair<string, IReadOnlyList<FieldValue>>> Values =>
        _values.Select(entry =>
            new KeyValuePair<string, IReadOnlyList<FieldValue>>(entry.Key, entry.Value));

    /// <summary>Replaces all values for <paramref name="propertyName"/> with a single (id, value) pair.</summary>
    public DynamicEntity Set(string propertyName, string id, object value)
    {
        var converted = ConvertValue(propertyName, value);
        _values[propertyName] = [new FieldValue(new EntityId(id), converted)];
        UpdateDirtyState(propertyName);
        return this;
    }

    /// <summary>Appends an additional (id, value) pair for <paramref name="propertyName"/>.</summary>
    public DynamicEntity Add(string propertyName, string id, object value)
    {
        var converted = ConvertValue(propertyName, value);
        if (!_values.TryGetValue(propertyName, out var values))
        {
            values = [];
            _values[propertyName] = values;
        }

        values.Add(new FieldValue(new EntityId(id), converted));
        UpdateDirtyState(propertyName);
        return this;
    }

    internal void AcceptChanges()
    {
        _originalValues.Clear();
        foreach (var entry in _values)
        {
            _originalValues[entry.Key] = [.. entry.Value];
        }

        _dirtyFieldIds.Clear();
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = GetValue(binder.Name);
        return result is not null;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        throw new NotSupportedException(
            $"Dynamic member assignment is not supported for '{binder.Name}' - call Set(name, id, value) so the originating record id is preserved for update/delete.");
    }

    private object ConvertValue(string propertyName, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_schema.Fields.TryGetValue(propertyName, out var field))
        {
            throw new NotSupportedException($"Property '{propertyName}' is not defined by the entity schema.");
        }

        var targetType = new SchemaTypeConverter().ResolveType(field.Type);
        if (value is string rawValue)
        {
            return SchemaTypeConverter.ConvertTo(rawValue, targetType);
        }

        if (value.GetType() == targetType)
        {
            return value;
        }

        throw new ArgumentException(
            $"Property '{propertyName}' must contain a value of type '{targetType.Name}'.",
            propertyName);
    }

    private void UpdateDirtyState(string propertyName)
    {
        var field = _schema.Fields[propertyName];
        if (ValuesEqual(GetFieldValuesFrom(_originalValues, propertyName), GetFieldValues(propertyName)))
        {
            _dirtyFieldIds.Remove(field.Id);
        }
        else
        {
            _dirtyFieldIds.Add(field.Id);
        }
    }

    private static IReadOnlyList<FieldValue> GetFieldValuesFrom(
        IReadOnlyDictionary<string, List<FieldValue>> values,
        string propertyName) =>
        values.TryGetValue(propertyName, out var propertyValues)
            ? propertyValues
            : [];

    private static bool ValuesEqual(IReadOnlyList<FieldValue> left, IReadOnlyList<FieldValue> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => Equals(pair.First, pair.Second));
}

public sealed record DynamicFieldState(
    string EntityType,
    string Name,
    string Id,
    string Type,
    bool IsRequired,
    FieldValue? OriginalValue,
    FieldValue? Value);
