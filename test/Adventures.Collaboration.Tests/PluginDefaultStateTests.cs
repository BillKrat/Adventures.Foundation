using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Collaboration.Tests;

/// <summary>A plug-in can say it starts switched off (a visitor-facing feature the owner opts into); an owner's switch then wins either way.</summary>
public sealed class PluginDefaultStateTests : IAsyncLifetime
{
    private IPlugins _plugins = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-016-plugins.nq"));
        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, PluginConstants.SchemaIri), PluginConstants.BaseIri, PluginConstants.TypeIri, CollaborationConstants.DefaultGraph, s => new Plugin(s));
        services.AddScoped<IEntityBll<Plugin>>(p => new EntityBll<Plugin>(new EntityDal<Plugin>(p.GetRequiredService<IEntityRepository<Plugin>>())));
        services.AddSingleton(new PluginDefinition("opt-in", "Opt in", "Starts off.", "1.0.0", 1, null, DefaultEnabled: false));
        services.AddSingleton(new PluginDefinition("normal", "Normal", "Starts on.", "1.0.0", 2));
        services.AddScoped<IPlugins, Plugins>();
        _plugins = services.BuildServiceProvider().GetRequiredService<IPlugins>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task APluginThatStartsOff_IsOff_UntilTheOwnerSwitchesItOn()
    {
        Assert.False(await _plugins.IsEnabledAsync("opt-in"));
        Assert.True(await _plugins.IsEnabledAsync("normal"));
        Assert.Equal(["normal"], await _plugins.EnabledKeysAsync());
        Assert.False(Assert.Single(await _plugins.ListAsync(), p => p.Key == "opt-in").Enabled);

        await _plugins.SetEnabledAsync("opt-in", true);

        Assert.True(await _plugins.IsEnabledAsync("opt-in"));
        Assert.Contains("opt-in", await _plugins.EnabledKeysAsync());
    }

    [Fact]
    public async Task TheOwnersSwitchOff_OfAPluginThatStartsOn_StillWins()
    {
        await _plugins.SetEnabledAsync("normal", false);

        Assert.False(await _plugins.IsEnabledAsync("normal"));
    }
}
