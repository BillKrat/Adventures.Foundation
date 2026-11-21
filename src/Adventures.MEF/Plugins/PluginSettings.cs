using System.Globalization;
using System.Text.Json.Serialization;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.Security;

namespace Adventures.MEF;

/// <summary>How a plug-in setting is entered and checked: free text, one value per line, true or false, a whole number, or a secret (a credential: encrypted at rest, write-only on the screen).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginSettingKind
{
    Text,
    Lines,
    Bool,
    Number,
    Secret,
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
    Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? clear = null, CancellationToken cancellationToken = default);

    /// <summary>What the Customize screen may show: every value as <see cref="GetAsync"/>, except secrets are empty, plus the names of the secrets that are set. A secret is never sent to a browser.</summary>
    Task<PluginSettingsView> GetForDisplayAsync(string pluginKey, CancellationToken cancellationToken = default);
}

/// <summary>The settings of one plug-in as the screen sees them: secrets are blank, and <see cref="SecretsSet"/> says which of them have a value.</summary>
public sealed record PluginSettingsView(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> SecretsSet);

public sealed class PluginSettings(IEntityBll<PluginSetting> states, EntitySchemaOf<PluginSetting> schema, IEnumerable<PluginDefinition> definitions, IFieldEncryptor? encryptor = null) : IPluginSettings
{
    private static string ContextOf(string pluginKey, string name) => $"plugin-setting:{pluginKey}:{name}";

    public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) =>
        (definitions.FirstOrDefault(p => string.Equals(p.Key, pluginKey, StringComparison.Ordinal))
            ?? throw new BusinessRuleException($"There is no plug-in '{pluginKey}'.")).Settings ?? [];

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default)
    {
        var declared = DefinitionsOf(pluginKey);
        var stored = await StoredAsync(pluginKey, cancellationToken).ConfigureAwait(false);
        return declared.ToDictionary(
            d => d.Name,
            d => stored.TryGetValue(d.Name, out var row) && row.GetValue("Value") is string value
                ? (d.Kind == PluginSettingKind.Secret ? Reveal(pluginKey, d.Name, value) : value)
                : (d.Kind == PluginSettingKind.Secret ? string.Empty : d.Default),
            StringComparer.Ordinal);
    }

    public async Task<PluginSettingsView> GetForDisplayAsync(string pluginKey, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(await GetAsync(pluginKey, cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
        var set = new List<string>();
        foreach (var d in DefinitionsOf(pluginKey).Where(d => d.Kind == PluginSettingKind.Secret))
        {
            if (values[d.Name].Length > 0)
            {
                set.Add(d.Name);
            }

            values[d.Name] = string.Empty;
        }

        return new PluginSettingsView(values, set);
    }

    /// <summary>The decrypted secret, or empty when it cannot be read (no key, another key, a changed value, or text that was never encrypted by us).</summary>
    private string Reveal(string pluginKey, string name, string stored) =>
        encryptor?.TryDecrypt(stored, ContextOf(pluginKey, name)) ?? string.Empty;

    public async Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? clear = null, CancellationToken cancellationToken = default)
    {
        var declared = DefinitionsOf(pluginKey).ToDictionary(d => d.Name, StringComparer.Ordinal);
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, raw) in values)
        {
            if (!declared.TryGetValue(name, out var definition))
            {
                throw new BusinessRuleException($"The plug-in '{pluginKey}' has no setting '{name}'.");
            }

            if (definition.Kind == PluginSettingKind.Secret)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue; // an empty secret field means keep what is there; clearing is its own request
                }

                var plain = CheckPlain(definition, raw);

                if (encryptor is null || !encryptor.IsConfigured)
                {
                    throw new BusinessRuleException($"{definition.Label} cannot be saved: encryption is not configured on this host (set Encryption:CurrentKeyId and Encryption:Keys:<id>). Nothing was stored.");
                }

                clean[name] = encryptor.Encrypt(plain, ContextOf(pluginKey, name));
                continue;
            }

            clean[name] = CheckPlain(definition, raw);
        }

        foreach (var name in clear ?? [])
        {
            if (!declared.TryGetValue(name, out var cleared) || cleared.Kind != PluginSettingKind.Secret)
            {
                throw new BusinessRuleException($"'{name}' is not a secret setting of '{pluginKey}', so it cannot be cleared this way.");
            }

            clean[name] = string.Empty;
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
    public static string CheckValue(PluginSettingDefinition definition, string? raw) =>
        definition.Kind == PluginSettingKind.Secret
            ? throw new BusinessRuleException($"{definition.Label} is a secret, and secrets are not supported for this kind of setting yet.")
            : CheckPlain(definition, raw);

    private static string CheckPlain(PluginSettingDefinition definition, string? raw)
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
