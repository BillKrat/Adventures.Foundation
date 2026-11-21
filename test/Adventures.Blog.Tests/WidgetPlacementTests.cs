using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// Where widgets sit: one row per placement (zone, widget, optional title, settings, position), kept per tenant. Only available widgets can be placed, in
/// one of the four zones, with settings the widget declared; moving swaps neighbours in a zone; a widget whose plug-in is off keeps its place but is
/// not shown, and shows again when the plug-in is switched on.
/// </summary>
public sealed class WidgetPlacementTests : IAsyncLifetime
{
    private sealed class EchoWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
            Task.FromResult($"<p>{context.Settings["Text"]}</p>");
    }

    private sealed class PlainWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => Task.FromResult("<p>plain</p>");
    }

    private sealed class EmptyWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => Task.FromResult("   ");
    }

    private sealed class BrokenWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("broken");
    }

    private sealed class MutablePlugins : IPlugins
    {
        public HashSet<string> On { get; } = ["p", HtmlSanitizerPlugin.Key];

        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(On.Contains(key));

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(On.ToHashSet());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private readonly MutablePlugins _plugins = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-025-widget-placements.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, WidgetConstants.SchemaIri), WidgetConstants.BaseIri, WidgetConstants.TypeIri, WidgetConstants.DefaultGraph, s => new WidgetPlacement(s));
        services.AddScoped<IEntityBll<WidgetPlacement>>(p => new EntityBll<WidgetPlacement>(p.GetRequiredService<IEntityDal<WidgetPlacement>>()));
        services.AddSingleton<IPlugins>(_plugins);
        services.AddScoped<IHtmlContentSanitizer, HtmlContentSanitizer>();
        services.AddWidgets(options => options.Timeout = TimeSpan.FromMilliseconds(500));
        services.AddScoped<IWidgetPlacements, WidgetPlacements>();
        services.AddWidget<EchoWidget>("p", new WidgetDefinition(
            "echo", "Echo", "Echoes its text.",
            [new PluginSettingDefinition("Text", "Text", "What to show.", PluginSettingKind.Text, "default text", 40), new PluginSettingDefinition("Count", "Count", "How many.", PluginSettingKind.Number, "5")]));
        services.AddWidget<PlainWidget>("p", new WidgetDefinition("plain", "Plain", "Plain."));
        services.AddWidget<BrokenWidget>("p", new WidgetDefinition("broken", "Broken", "Always fails."));
        services.AddWidget<EmptyWidget>("p", new WidgetDefinition("empty", "Empty", "Has nothing to show."));
        services.AddWidget<PlainWidget>("off", new WidgetDefinition("hidden", "Hidden", "Belongs to a plug-in that is off."));
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IWidgetPlacements Placements() => _services.CreateScope().ServiceProvider.GetRequiredService<IWidgetPlacements>();

    [Fact]
    public async Task APlacedWidget_IsListed_InItsZone_WithItsSettings_AndTheNextPosition()
    {
        var first = await Placements().AddAsync("Left", "plain", null, null);
        var second = await Placements().AddAsync("left", "echo", "My echo", new Dictionary<string, string> { ["Text"] = "  hi  " });

        Assert.Equal(("left", "plain", 1), (first.Zone, first.WidgetKey, first.Position));
        Assert.Equal(("left", "echo", "My echo", 2), (second.Zone, second.WidgetKey, second.Title, second.Position));
        Assert.Equal("hi", second.Settings["Text"]);
        Assert.Equal([first.Id, second.Id], (await Placements().ListAsync()).Select(p => p.Id));
    }

    [Theory]
    [InlineData("sidebar", "plain")]
    [InlineData("left", "nope")]
    [InlineData("left", "hidden")]
    public async Task AnUnknownZone_AnUnknownWidget_OrAWidgetOfAPluginThatIsOff_CannotBePlaced(string zone, string widget)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Placements().AddAsync(zone, widget, null, null));
    }

    [Fact]
    public async Task Settings_AreCheckedAgainstWhatTheWidgetDeclared()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Placements().AddAsync("left", "echo", null, new Dictionary<string, string> { ["Count"] = "many" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Placements().AddAsync("left", "echo", null, new Dictionary<string, string> { ["Bogus"] = "x" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Placements().AddAsync("left", "echo", null, new Dictionary<string, string> { ["Text"] = new string('x', 41) }));
        Assert.Empty(await Placements().ListAsync());
    }

    [Fact]
    public async Task TheList_IsInZoneOrder_ThenPosition()
    {
        var footer = await Placements().AddAsync("footer", "plain", null, null);
        var left1 = await Placements().AddAsync("left", "plain", null, null);
        var header = await Placements().AddAsync("header", "plain", null, null);
        var left2 = await Placements().AddAsync("left", "echo", null, null);

        Assert.Equal([header.Id, left1.Id, left2.Id, footer.Id], (await Placements().ListAsync()).Select(p => p.Id));
    }

    [Fact]
    public async Task Moving_SwapsNeighboursInTheZone_AndNothingHappensAtTheEdge()
    {
        var a = await Placements().AddAsync("right", "plain", null, null);
        var b = await Placements().AddAsync("right", "echo", null, null);
        var c = await Placements().AddAsync("right", "plain", null, null);
        var other = await Placements().AddAsync("left", "plain", null, null);

        await Placements().MoveAsync(c.Id, -1);
        Assert.Equal([a.Id, c.Id, b.Id], (await Placements().ListAsync()).Where(p => p.Zone == "right").Select(p => p.Id));

        await Placements().MoveAsync(a.Id, -1);
        await Placements().MoveAsync(b.Id, +1);
        Assert.Equal([a.Id, c.Id, b.Id], (await Placements().ListAsync()).Where(p => p.Zone == "right").Select(p => p.Id));
        Assert.Equal(1, (await Placements().ListAsync()).Single(p => p.Id == other.Id).Position);
        Assert.False(await Placements().MoveAsync("no-such-id", 1));
    }

    [Fact]
    public async Task Updating_ChangesTheTitleAndSettings_AndRemovingDeletesThePlacement()
    {
        var placed = await Placements().AddAsync("left", "echo", null, new Dictionary<string, string> { ["Text"] = "one" });

        var updated = await Placements().UpdateAsync(placed.Id, "Renamed", new Dictionary<string, string> { ["Text"] = "two" });

        Assert.Equal(("Renamed", "two"), (updated!.Title, updated.Settings["Text"]));
        Assert.True(await Placements().RemoveAsync(placed.Id));
        Assert.Empty(await Placements().ListAsync());
        Assert.False(await Placements().RemoveAsync(placed.Id));
        Assert.Null(await Placements().UpdateAsync("no-such-id", "x", null));
    }

    [Fact]
    public async Task ARenderedZone_HasEachPlacedWidgetsHtml_InOrder_WithItsTitle_AndSkipsAFailingWidget()
    {
        await Placements().AddAsync("left", "echo", "Shown title", new Dictionary<string, string> { ["Text"] = "first" });
        await Placements().AddAsync("left", "broken", null, null);
        await Placements().AddAsync("left", "plain", null, null);
        await Placements().AddAsync("right", "plain", null, null);

        var rendered = await Placements().RenderZoneAsync("left");

        Assert.Equal(["echo", "plain"], rendered.Select(r => r.WidgetKey));
        Assert.Equal(["Shown title", "Plain"], rendered.Select(r => r.Title));
        Assert.Equal("<p>first</p>", rendered[0].Html);
    }

    [Fact]
    public async Task AWidgetWhosePluginIsSwitchedOff_KeepsItsPlace_IsNotShown_AndReturnsWhenSwitchedOn()
    {
        var placed = await Placements().AddAsync("left", "plain", null, null);

        _plugins.On.Remove("p");
        Assert.Empty(await Placements().RenderZoneAsync("left"));
        Assert.False((await Placements().ListAsync()).Single().Available);

        _plugins.On.Add("p");
        Assert.Equal(placed.Id, (await Placements().RenderZoneAsync("left")).Single().Id);
        Assert.True((await Placements().ListAsync()).Single().Available);
    }

    [Fact]
    public async Task AnInvalidZone_RendersNothing()
    {
        await Placements().AddAsync("left", "plain", null, null);

        Assert.Empty(await Placements().RenderZoneAsync("sidebar"));
    }

    [Fact]
    public async Task AWidgetWithNothingToShow_IsLeftOutOfTheZone_SoNoEmptyBoxIsDrawn()
    {
        await Placements().AddAsync("left", "empty", null, null);
        await Placements().AddAsync("left", "plain", null, null);

        Assert.Equal(["plain"], (await Placements().RenderZoneAsync("left")).Select(r => r.WidgetKey));
    }
}
