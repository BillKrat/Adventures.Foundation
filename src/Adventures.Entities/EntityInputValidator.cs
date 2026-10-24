// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// Validates the raw, untrusted strings of an <see cref="EntityDataModel"/> against an <see cref="EntitySchema"/> before
/// any of it touches an entity: every field must be declared by the schema, every non-empty value must parse as the
/// declared type, and (on create) every required field must be present. Messages are written here, never taken from a
/// parser's exception, so nothing internal reaches the client. Empty values are not errors for optional fields - the
/// presenter treats them as "not supplied" (a browser form sends "" for an untouched input).
/// </summary>
public static class EntityInputValidator
{
    private static readonly SchemaTypeConverter Converter = new();

    /// <summary>Returns every error by field name; an empty dictionary means the input is acceptable.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(EntitySchema schema, EntityDataModel data, bool isCreate)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(data);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in data.Values)
        {
            if (name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!schema.Fields.TryGetValue(name, out var field))
            {
                errors[name] = [$"'{name}' is not a field of this entity."];
                continue;
            }

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!TryParse(value, field.Type))
            {
                errors[name] = [$"The {name} field is not in the right format. Expected {Describe(field.Type)}."];
            }
        }

        if (isCreate)
        {
            foreach (var field in schema.Fields.Values.Where(f =>
                         f.IsRequired && !f.Name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase)))
            {
                var supplied = data.Values.TryGetValue(field.Name, out var value) && !string.IsNullOrEmpty(value);
                if (!supplied)
                {
                    errors[field.Name] = [$"{field.Name} is required."];
                }
            }
        }

        return errors;
    }

    /// <summary>Throws <see cref="InputValidationException"/> when <see cref="Validate"/> finds any error.</summary>
    public static void ThrowIfInvalid(EntitySchema schema, EntityDataModel data, bool isCreate)
    {
        var errors = Validate(schema, data, isCreate);
        if (errors.Count > 0)
        {
            throw new InputValidationException(errors);
        }
    }

    /// <summary>What a person should type for a schema type, for the error message.</summary>
    private static string Describe(string typeName) => typeName.ToLowerInvariant() switch
    {
        "date" => "a date such as 1961-11-19",
        "datetime" or "datetimeoffset" => "a date and time such as 1961-11-19T14:30:00Z",
        "int" or "integer" or "long" => "a whole number",
        "double" or "decimal" => "a number such as 12.5",
        "bool" or "boolean" => "true or false",
        "guid" => "an identifier such as 123e4567-e89b-12d3-a456-426614174000",
        _ => $"a valid {typeName}",
    };

    private static bool TryParse(string raw, string typeName)
    {
        try
        {
            SchemaTypeConverter.ConvertTo(raw, Converter.ResolveType(typeName));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
