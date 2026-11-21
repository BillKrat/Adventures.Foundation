using System.Composition;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF.SamplePlugin;

/// <summary>What a developer's plug-in looks like: one exported <see cref="IPluginModule"/> that says what it is and registers what it adds.</summary>
[Export(typeof(IPluginModule))]
public sealed class SamplePluginModule : IPluginModule
{
    public PluginDefinition Definition => new("sample", "Sample plug-in", "A plug-in that only proves the seam from outside the app.", "0.1.0", 900);

    public void ConfigureServices(IServiceCollection services) => services.AddSingleton(new SampleMarker("hello from the sample plug-in"));
}

/// <summary>Registered by the sample plug-in so a test can see that its services reached the host.</summary>
public sealed record SampleMarker(string Text);
