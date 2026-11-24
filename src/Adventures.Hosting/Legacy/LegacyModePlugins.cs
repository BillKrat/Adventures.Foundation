using Adventures.Collaboration;
using Adventures.Common.Exceptions;
using Adventures.MEF;

namespace Adventures.Hosting;

/// <summary>
/// The plug-in switches in the legacy data mode: the legacy tables have no place for collaboration items, so its three plug-ins are always off, every
/// other plug-in is on, and nothing can be switched. There is no stored state (and no tenant at start-up), which is why this is not a step that writes
/// "off" into the store.
/// </summary>
public sealed class LegacyModePlugins(IEnumerable<PluginDefinition> definitions) : IPlugins
{
    private static readonly HashSet<string> AlwaysOff = CollaborationPlugins.Definitions.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

    public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginState>>(definitions
            .Select(p => new PluginState(p.Key, p.Title, p.Description, p.Version, p.Priority, !AlwaysOff.Contains(p.Key), p.Settings is { Count: > 0 }))
            .OrderBy(p => p.Priority)
            .ToList());

    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(!AlwaysOff.Contains(key));

    public async Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.Enabled).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("In the legacy data mode the collaboration plug-ins are always off and plug-ins cannot be switched.");
}
