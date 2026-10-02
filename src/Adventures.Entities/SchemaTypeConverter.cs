using System.Globalization;

namespace Adventures.Entities;

/// <summary>
/// Resolves a schema-declared type-name string (e.g. "boolean", "guid") to a CLR
/// <see cref="Type"/>, and converts a raw stored string into an instance of that type.
/// <see cref="Convert.ChangeType(object, Type)"/> alone cannot cover this: <see cref="Guid"/>,
/// <see cref="DateTimeOffset"/>, and enums do not implement <see cref="IConvertible"/>, so calling
/// it directly on them throws a generic <see cref="InvalidCastException"/> unrelated to any real
/// schema mismatch. Every conversion below is explicitly <see cref="CultureInfo.InvariantCulture"/> -
/// the default (current-thread culture) would parse the same stored value differently depending on
/// server locale, which is unacceptable for an audited/reproducible system.
/// Instance-based, not static, so a caller can add application-specific type names (e.g. an
/// enum) without mutating shared global state that other callers/tests would also see.
/// </summary>
public sealed class SchemaTypeConverter
{
    private readonly Dictionary<string, Type> _typeMap;

    public SchemaTypeConverter(IEnumerable<KeyValuePair<string, Type>>? additionalTypes = null)
    {
        _typeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "bool", typeof(bool) },
            { "boolean", typeof(bool) },
            { "int", typeof(int) },
            { "integer", typeof(int) },
            { "long", typeof(long) },
            { "double", typeof(double) },
            { "decimal", typeof(decimal) },
            { "string", typeof(string) },
            { "datetime", typeof(DateTime) },
            { "datetimeoffset", typeof(DateTimeOffset) },
            { "date", typeof(DateOnly) },
            { "guid", typeof(Guid) },
        };

        if (additionalTypes is not null)
        {
            foreach (var entry in additionalTypes)
            {
                _typeMap[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>
    /// Resolves a schema type-name to its CLR <see cref="Type"/>. Throws
    /// <see cref="NotSupportedException"/> for an unknown name - a schema referencing a type
    /// this converter does not know is a contract violation, not something to guess past.
    /// </summary>
    public Type ResolveType(string typeName) =>
        _typeMap.TryGetValue(typeName, out var type)
            ? type
            : throw new NotSupportedException($"No CLR type is registered for schema type name '{typeName}'.");

    /// <summary>Resolves <paramref name="typeName"/> then converts <paramref name="raw"/> to it.</summary>
    public object ConvertTo(string raw, string typeName) => ConvertTo(raw, ResolveType(typeName));

    /// <summary>
    /// Converts <paramref name="raw"/> to an instance of <paramref name="targetType"/>. Special-
    /// cases the types <see cref="Convert.ChangeType(object, Type)"/> cannot handle; falls
    /// through to it for everything else (int, long, double, decimal, bool, string, ...).
    /// </summary>
    public static object ConvertTo(string raw, Type targetType) => targetType switch
    {
        _ when targetType == typeof(Guid) => Guid.Parse(raw),
        _ when targetType == typeof(DateTimeOffset) => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(DateTime) => DateTime.Parse(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(DateOnly) => DateOnly.ParseExact(
            raw,
            ["MM-dd-yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None),
        _ when targetType.IsEnum => Enum.Parse(targetType, raw, ignoreCase: true),
        _ => Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture),
    };
}
