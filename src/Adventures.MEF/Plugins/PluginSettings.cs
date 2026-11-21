using System.Globalization;
using System.Text.Json.Serialization;
using Adventures.Common.Exceptions;
using Adventures.Entities;

namespace Adventures.MEF;

/// <summary>How a plug-in setting is entered and checked: free text, one value per line, true or false, or a whole number.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginSettingKind
{
    Text,
    Lines,
    Bool,
    Number,
}

/// <summary>
/// One setting a plug-in declares for its Customize screen. <see cref="Validate"/> is the plug-in's own rule (null when the value is fine, otherwise the
/// message shown to the administrator); it is not sent to the browser. The value is checked as a whole before anything is stored.
/// </summary>
public sealed record PluginSettingDefinition(
    string Name,
    string Label,
    string Description,
    PluginSettingKind Kind,
    string Default,
    int MaxLength = 500,
    [property: JsonIgnore] Func<string, string?>? Validate = null);

/// <summary>One stored plug-in setting (one row per plug-in key and setting name); its schema ships in Sql/seed/seed-024-plugin-settings.nq.</summary>
public sealed class PluginSetting(EntitySchema schema) : DynamicEntity(schema);

/// <summary>Reads and changes a plug-in's settings. Stored per tenant when the host registers a tenant-scoped repository for <see cref="PluginSetting"/>.</summary>
public interface IPluginSettings
{
    /// <summary>The settings the plug-in declares; a business-rule error for an unknown plug-in.</summary>
    IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey);

    /// <summary>Every declared setting with its stored value, or its default when none is stored.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default);

    /// <summary>Validates every given value (and that the plug-in declares it), then stores them all; one bad value stores nothing.</summary>
    Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
}

public sealed class PluginSettings(IEntityBll<PluginSetting> states, EntitySchemaOf<PluginSetting> schema, IEnumerable<PluginDefinition> definitions) : IPluginSettings
{
    public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) =>
        (definitions.FirstOrDefault(p => string.Equals(p.Key, pluginKey, StringComparison.Ordinal))
            ?? throw new BusinessRuleException($"There is no plug-in '{pluginKey}'.")).Settings ?? [];

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default)
    {
        var declared = DefinitionsOf(pluginKey);
        var stored = await StoredAsync(pluginKey, cancellationToken).ConfigureAwait(false);
        return declared.ToDictionary(
            d => d.Name,
            d => stored.TryGetValue(d.Name, out var row) && row.GetValue("Value") is string value ? value : d.Default,
            StringComparer.Ordinal);
    }

    public async Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        var declared = DefinitionsOf(pluginKey).ToDictionary(d => d.Name, StringComparer.Ordinal);
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, raw) in values)
        {
            if (!declared.TryGetValue(name, out var definition))
            {
                throw new BusinessRuleException($"The plug-in '{pluginKey}' has no setting '{name}'.");
            }

            clean[name] = Check(definition, raw);
        }

        var stored = await StoredAsync(pluginKey, cancellationToken).ConfigureAwait(false);
        foreach (var (name, value) in clean)
        {
            if (stored.TryGetValue(name, out var existing))
            {
                existing.Set("Value", Guid.NewGuid().ToString(), value);
                await states.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var row = new PluginSetting(schema.Schema);
            row.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
            row.Set("PluginKey", Guid.NewGuid().ToString(), pluginKey);
            row.Set("Name", Guid.NewGuid().ToString(), name);
            row.Set("Value", Guid.NewGuid().ToString(), value);
            await states.CreateAsync(row, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The value in its stored form, or a business-rule error saying what is wrong with it.</summary>
    private static string Check(PluginSettingDefinition definition, string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length > definition.MaxLength)
        {
            throw new BusinessRuleException($"{definition.Label} can be at most {definition.MaxLength} characters.");
        }

        switch (definition.Kind)
        {
            case PluginSettingKind.Bool when !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase):
                throw new BusinessRuleException($"{definition.Label} must be true or false.");
            case PluginSettingKind.Bool:
                value = value.ToLowerInvariant();
                break;
            case PluginSettingKind.Number when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _):
                throw new BusinessRuleException($"{definition.Label} must be a whole number.");
        }

        if (definition.Validate?.Invoke(value) is { Length: > 0 } problem)
        {
            throw new BusinessRuleException(problem);
        }

        return value;
    }

    private async Task<Dictionary<string, PluginSetting>> StoredAsync(string pluginKey, CancellationToken cancellationToken) =>
        (await states.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.GetValue("PluginKey") as string == pluginKey && r.GetValue("Name") is string)
            .GroupBy(r => (string)r.GetValue("Name")!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}
