using Adventures.Entities;

namespace Adventures.MEF;

/// <summary>The seeds of the plug-in on/off state and plug-in settings.</summary>
public static class PluginSeeds
{
    /// <summary>In application order.</summary>
    public static IReadOnlyList<SeedEntry> All { get; } =
    [
        new("seed-016-plugins.nq", PluginConstants.SeedName),
        new("seed-024-plugin-settings.nq", PluginConstants.SettingSeedName),
    ];
}

