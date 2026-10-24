using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>The built-in plug-ins: on until switched off, state kept, a switched-off collaboration kind refuses new items.</summary>
public sealed class PluginTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IPlugins _plugins = null!;
    private ICollaborationBll _items = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-015-collaboration.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-016-plugins.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, PluginConstants.SchemaIri), PluginConstants.BaseIri, PluginConstants.TypeIri, CollaborationConstants.DefaultGraph, s => new Plugin(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, CollaborationConstants.SchemaIri), CollaborationConstants.BaseIri, CollaborationConstants.TypeIri, CollaborationConstants.DefaultGraph, s => new CollaborationItem(s));
        services.AddScoped<IEntityBll<Plugin>>(p => new EntityBll<Plugin>(p.GetRequiredService<IEntityRepository<Plugin>>()));
        services.AddScoped<IPlugins, Plugins>();
        services.AddScoped<ICollaborationBll, CollaborationBll>();
        _services = services.BuildServiceProvider();
        _plugins = _services.GetRequiredService<IPlugins>();
        _items = _services.GetRequiredService<ICollaborationBll>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CollaborationItem NewItem(string kind)
    {
        var item = new CollaborationItem(_services.GetRequiredService<EntitySchemaOf<CollaborationItem>>().Schema);
        item.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        item.Set("Title", Guid.NewGuid().ToString(), "An item");
        item.Set("Kind", Guid.NewGuid().ToString(), kind);
        return item;
    }

    [Fact]
    public async Task EveryBuiltInPluginIsOnUntilSwitchedOff()
    {
        var all = await _plugins.ListAsync();

        Assert.Equal(["developer-collaboration", "research-collaboration", "decisions"], all.Select(p => p.Key));
        Assert.All(all, p => Assert.True(p.Enabled));
        Assert.True(await _plugins.IsEnabledAsync("developer-collaboration"));
        Assert.True(await _plugins.IsEnabledAsync("not-a-plugin"));
    }

    [Fact]
    public async Task SwitchingOffAndOnIsKept_AndAnUnknownKeyIsRefused()
    {
        await _plugins.SetEnabledAsync("decisions", false);
        Assert.False(await _plugins.IsEnabledAsync("decisions"));
        Assert.DoesNotContain("decisions", await _plugins.EnabledKeysAsync());

        await _plugins.SetEnabledAsync("decisions", true);
        Assert.True(await _plugins.IsEnabledAsync("decisions"));
        Assert.Single((await _plugins.ListAsync()).Where(p => p.Key == "decisions"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _plugins.SetEnabledAsync("nope", false));
    }

    [Fact]
    public async Task ASwitchedOffKindRefusesNewItems_ButTheOtherKindStillWorks_AndDataIsKept()
    {
        var kept = await _items.CreateAsync(NewItem(CollaborationKinds.Research));
        await _plugins.SetEnabledAsync("research-collaboration", false);

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => _items.CreateAsync(NewItem(CollaborationKinds.Research)));

        Assert.Contains("Research Collaboration", refusal.Message);
        Assert.NotNull(await _items.CreateAsync(NewItem(CollaborationKinds.Developer)));
        Assert.NotNull(await _items.GetAsync(kept.EntityId!.Value));
    }
}
