namespace Adventures.Data;

/// <summary>
/// Which data layer a host runs on. <see cref="New"/> is the schema-driven N-Quad store; <see cref="Legacy"/> reads and writes the
/// BlogEngine.NET SQL Server tables directly. The host changes registrations only (which Dal serves each entity type); no Bll, presenter
/// or controller knows the mode. Collaboration has no legacy tables and is switched off in Legacy.
/// </summary>
public enum DataMode
{
    New = 0,
    Legacy = 1,
}

public static class DataModes
{
    /// <summary>The configuration key (environment variable <c>Data__Mode</c>).</summary>
    public const string SettingName = "Data:Mode";

    /// <summary>Parses the setting; empty or missing is <see cref="DataMode.New"/>, an unknown value throws rather than silently running the wrong store.</summary>
    public static DataMode Parse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DataMode.New
        : Enum.GetNames<DataMode>().FirstOrDefault(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase)) is { } named ? Enum.Parse<DataMode>(named)
        : throw new InvalidOperationException($"{SettingName} must be 'New' or 'Legacy', not '{value}'.");
}
