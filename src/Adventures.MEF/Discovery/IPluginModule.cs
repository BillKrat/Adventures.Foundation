using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF;

/// <summary>
/// The entry point of a plug-in: what it is, and what it adds to the host's services (tools, widgets, anything it registers). A developer exports
/// one from a project the host references or from a DLL dropped in the plug-in directory:
/// <c>[Export(typeof(IPluginModule))] public sealed class MyPlugin : IPluginModule</c> (the attribute comes from System.Composition).
/// </summary>
public interface IPluginModule
{
    /// <summary>The plug-in key, title and version, as the Plugins page shows them. The key must be unique in the host.</summary>
    PluginDefinition Definition { get; }

    /// <summary>Registers what the plug-in contributes. Called once at start-up for each plug-in that loaded.</summary>
    void ConfigureServices(IServiceCollection services);
}
