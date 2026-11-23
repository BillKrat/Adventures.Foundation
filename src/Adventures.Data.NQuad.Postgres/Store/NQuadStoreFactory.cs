namespace Adventures.Data.NQuad;

/// <summary>
/// Chooses the PostgreSQL layout of the N-Quad store from the host's settings: <c>NQuadStore:Layout</c> = <c>Text</c> (the default: today's single text table <c>NQuadStore:Table</c>, default
/// "app_n_quads") or <c>Dictionary</c> (the hybrid-dictionary layout, tables named from <c>NQuadStore:Prefix</c>, default "app_dq"). The two layouts keep their data in different tables, so switching
/// the setting never reads or changes the other layout's data: moving data across is a deliberate, verified step (the pristine-store cutover), not a side effect of a setting.
/// </summary>
public static class NQuadStoreFactory
{
    public const string Text = "Text";
    public const string Dictionary = "Dictionary";
    public const string DefaultTextTable = "app_n_quads";
    public const string DefaultDictionaryPrefix = "app_dq";

    public static INQuadStore CreatePostgres(string connectionString, string? layout, string? textTable, string? dictionaryPrefix)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        if (string.IsNullOrWhiteSpace(layout) || string.Equals(layout, Text, StringComparison.OrdinalIgnoreCase))
        {
            return new NpgsqlNQuadStore(connectionString, string.IsNullOrWhiteSpace(textTable) ? DefaultTextTable : textTable);
        }

        if (string.Equals(layout, Dictionary, StringComparison.OrdinalIgnoreCase))
        {
            return new NpgsqlDictionaryNQuadStore(connectionString, string.IsNullOrWhiteSpace(dictionaryPrefix) ? DefaultDictionaryPrefix : dictionaryPrefix);
        }

        throw new InvalidOperationException($"NQuadStore:Layout must be '{Text}' or '{Dictionary}', not '{layout}'.");
    }
}
