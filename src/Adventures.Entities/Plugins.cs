// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// The stored on/off state of one built-in plug-in (see <see cref="PluginCatalog"/>). A plug-in with no stored state is enabled, so a
/// system that has never opened the Plugins page behaves exactly as before. Its schema ships in Sql/seed/seed-016-plugins.nq.
/// </summary>
public sealed class Plugin(EntitySchema schema) : DynamicEntity(schema);

/// <summary>Where a plug-in state lives in the store.</summary>
public static class PluginConstants
{
    public const string SeedName = "016-plugins";
    public const string SchemaIri = "https://schema.global-webnet.com/collaboration/Plugin";
    public const string BaseIri = "https://global-webnet.com/id/plugin/";
    public const string TypeIri = "https://global-webnet.com/ontology/collaboration#Plugin";
}

/// <summary>One built-in plug-in as the code declares it: what it is, and what it contributes to the admin.</summary>
public sealed record PluginDefinition(string Key, string Title, string Description, string Version, int Priority);

/// <summary>
/// The built-in plug-ins. A plug-in is a bundle of contributions (an admin menu entry, a schema, endpoints, MCP tools); switching one off
/// hides its menu entry and makes its endpoints refuse, and keeps its data. Plug-ins loaded from outside the app (MEF) are a later step; the
/// stored state and the Plugins page do not change when they arrive.
/// </summary>
public static class PluginCatalog
{
    public const string DeveloperCollaboration = "developer-collaboration";
    public const string ResearchCollaboration = "research-collaboration";
    public const string Decisions = "decisions";

    public static readonly IReadOnlyList<PluginDefinition> BuiltIn =
    [
        new(DeveloperCollaboration, "Developer Collaboration", "Review requests between the people and AIs building the project: branch, repositories, test steps, Approve and Request changes.", "1.0", 0),
        new(ResearchCollaboration, "Research Collaboration", "Research questions and findings: a conversation with sources, accepted into a post when settled. No approval.", "1.0", 1),
        new(Decisions, "Decisions", "The page listing every decision recorded in a conversation, across posts and collaboration items.", "1.0", 2),
    ];

    /// <summary>The plug-in that allows items of this collaboration kind.</summary>
    public static string? ForKind(string kind) => kind switch
    {
        CollaborationKinds.Developer => DeveloperCollaboration,
        CollaborationKinds.Research => ResearchCollaboration,
        _ => null,
    };
}

/// <summary>A built-in plug-in with its current state, as the Plugins page shows it.</summary>
public sealed record PluginState(string Key, string Title, string Description, string Version, int Priority, bool Enabled);

/// <summary>Reads and changes which built-in plug-ins are on. The host decides who may change them.</summary>
public interface IPlugins
{
    Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the plug-in is on (also when it has never been switched, and for a key that is not a built-in plug-in).</summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>The keys of the plug-ins that are on.</summary>
    Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Switches a built-in plug-in on or off; refused (a business-rule error) for an unknown key.</summary>
    Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IPlugins"/> over the stored states and the catalog.</summary>
public sealed class Plugins(IEntityBll<Plugin> states, EntitySchemaOf<Plugin> schema) : IPlugins
{
    public async Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await StoredAsync(cancellationToken).ConfigureAwait(false);
        return PluginCatalog.BuiltIn
            .Select(p => new PluginState(p.Key, p.Title, p.Description, p.Version, p.Priority, !stored.TryGetValue(p.Key, out var state) || IsOn(state)))
            .OrderBy(p => p.Priority)
            .ToList();
    }

    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
        (await StoredAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(key, out var state) ? IsOn(state) : true;

    public async Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.Enabled).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public async Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        var definition = PluginCatalog.BuiltIn.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))
            ?? throw new BusinessRuleException($"There is no plug-in '{key}'.");
        var stored = await StoredAsync(cancellationToken).ConfigureAwait(false);
        if (stored.TryGetValue(definition.Key, out var existing))
        {
            existing.Set("Enabled", Guid.NewGuid().ToString(), enabled);
            await states.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            return;
        }

        var plugin = new Plugin(schema.Schema);
        plugin.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        plugin.Set("Key", Guid.NewGuid().ToString(), definition.Key);
        plugin.Set("Title", Guid.NewGuid().ToString(), definition.Title);
        plugin.Set("Version", Guid.NewGuid().ToString(), definition.Version);
        plugin.Set("Priority", Guid.NewGuid().ToString(), definition.Priority);
        plugin.Set("Enabled", Guid.NewGuid().ToString(), enabled);
        await states.CreateAsync(plugin, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsOn(Plugin state) => state.GetValue("Enabled") is not false;

    private async Task<Dictionary<string, Plugin>> StoredAsync(CancellationToken cancellationToken) =>
        (await states.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => p.GetValue("Key") is string)
            .GroupBy(p => (string)p.GetValue("Key")!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}
