using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// A widget returns HTML for a zone. The host decides what reaches the page: only widgets of plug-ins that are on, only the four zones, the settings
/// the widget declared (with their defaults), a time limit, and the HTML goes through the sanitiser plug-in like post HTML. A widget that fails or hangs
/// is left out and logged; it never breaks the page.
/// </summary>
public sealed class WidgetRendererTests
{
    private sealed class HtmlWidget(string html) : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => Task.FromResult(html);
    }

    private sealed class UnsafeWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => Task.FromResult("<p>hi</p><script>alert(1)</script><img src=x onerror=alert(1)>");
    }

    private sealed class ThrowingWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("the feed is down");
    }

    private sealed class HangingWidget : IWidget
    {
        public async Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite);
            return "never";
        }
    }

    private sealed class EchoWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
            Task.FromResult($"<p>{context.Zone}|{string.Join(",", context.Settings.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value))}|{context.Tenant}</p>");
    }

    private sealed class FakePlugins(params string[] on) : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(on.Contains(key));

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(on.ToHashSet());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static IWidgetRenderer Renderer(Action<IServiceCollection> register, params string[] enabledPlugins)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPlugins>(_ => new FakePlugins(enabledPlugins));
        services.AddScoped<IHtmlContentSanitizer, HtmlContentSanitizer>();
        services.AddWidgets(options => options.Timeout = TimeSpan.FromMilliseconds(250));
        register(services);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IWidgetRenderer>();
    }

    private static WidgetDefinition Definition(string key, params PluginSettingDefinition[] settings) => new(key, key, "d", settings);

    private static readonly IReadOnlyDictionary<string, string> NoSettings = new Dictionary<string, string>();

    [Fact]
    public void TheZones_AreTheFour_AndNameCheckingIgnoresCase()
    {
        Assert.Equal(["header", "left", "right", "footer"], WidgetZones.All);
        Assert.True(WidgetZones.IsValid("Footer"));
        Assert.False(WidgetZones.IsValid("sidebar"));
        Assert.False(WidgetZones.IsValid(null));
    }

    [Fact]
    public async Task AWidgetsHtml_IsSanitised_LikePostHtml()
    {
        var renderer = Renderer(s => s.AddWidget<UnsafeWidget>("p", Definition("unsafe")), "p", HtmlSanitizerPlugin.Key);

        var html = await renderer.RenderAsync("unsafe", "left", NoSettings);

        Assert.Contains("<p>hi</p>", html);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhenTheSanitiserPluginIsOff_TheWidgetsHtmlPassesAsWritten()
    {
        var on = Renderer(s => s.AddWidget<UnsafeWidget>("p", Definition("unsafe")), "p", HtmlSanitizerPlugin.Key);
        var off = Renderer(s => s.AddWidget<UnsafeWidget>("p", Definition("unsafe")), "p");

        Assert.DoesNotContain("<script>", (await on.RenderAsync("unsafe", "left", NoSettings))!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<script>", (await off.RenderAsync("unsafe", "left", NoSettings))!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AWidgetOfAPluginThatIsOff_IsNotAvailable_AndDoesNotRender()
    {
        var renderer = Renderer(s =>
        {
            s.AddWidget<HtmlWidget>("on", Definition("a"));
            s.AddWidget<HtmlWidget>("off", Definition("b"));
        }, "on");

        var available = await renderer.AvailableAsync();

        Assert.Equal(["a"], available.Select(w => w.Definition.Key));
        Assert.Null(await renderer.RenderAsync("b", "left", NoSettings));
    }

    [Theory]
    [InlineData("nope", "left")]
    [InlineData("a", "sidebar")]
    [InlineData("a", "")]
    public async Task AnUnknownWidgetOrZone_RendersNothing(string widget, string zone)
    {
        var renderer = Renderer(s => s.AddWidget<HtmlWidget>("p", Definition("a")), "p");

        Assert.Null(await renderer.RenderAsync(widget, zone, NoSettings));
    }

    [Fact]
    public async Task AWidgetThatThrows_OrHangs_IsLeftOut_WithoutBreakingTheOthers()
    {
        var renderer = Renderer(s =>
        {
            s.AddWidget<ThrowingWidget>("p", Definition("throws"));
            s.AddWidget<HangingWidget>("p", Definition("hangs"));
            s.AddWidget<UnsafeWidget>("p", Definition("fine"));
        }, "p");

        Assert.Null(await renderer.RenderAsync("throws", "left", NoSettings));
        Assert.Null(await renderer.RenderAsync("hangs", "left", NoSettings));
        Assert.NotNull(await renderer.RenderAsync("fine", "left", NoSettings));
    }

    [Fact]
    public async Task TheWidget_GetsItsDeclaredSettings_WithDefaults_AndNothingItDidNotDeclare()
    {
        var renderer = Renderer(s => s.AddWidget<EchoWidget>("p", Definition(
            "echo",
            new PluginSettingDefinition("Title", "Title", "d", PluginSettingKind.Text, "Default title"),
            new PluginSettingDefinition("Count", "Count", "d", PluginSettingKind.Number, "5"))), "p");

        var html = await renderer.RenderAsync("echo", "Right", new Dictionary<string, string> { ["Count"] = "9", ["Sneaky"] = "x" });

        Assert.Equal("<p>right|Count=9,Title=Default title|</p>", html);
    }

    [Fact]
    public async Task AHugeAnswer_IsCutOff()
    {
        var renderer = Renderer(s => s.AddWidget<HtmlWidget>("p", Definition("big")).AddSingleton(new HtmlWidget("<p>" + new string('x', 300_000) + "</p>")), "p");

        var html = await renderer.RenderAsync("big", "left", NoSettings);

        Assert.True(html is null || html.Length <= WidgetRenderOptions.DefaultMaxLength + 100);
    }
}
