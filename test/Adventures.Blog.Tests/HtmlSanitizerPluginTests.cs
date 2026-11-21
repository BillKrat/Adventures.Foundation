using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// HTML sanitising is a plug-in that is on by default, behind the <see cref="IHtmlContentSanitizer"/> seam: on, it is today's fixed rules; off
/// (an owner's choice, per tenant), HTML passes through as written; a host or developer can replace the seam. Anything that goes wrong in
/// reading the switch must fall on the safe side (sanitise).
/// </summary>
public sealed class HtmlSanitizerPluginTests
{
    private static IServiceProvider With(IPlugins plugins) => new ServiceCollection().AddSingleton(plugins).BuildServiceProvider();

    private const string Unsafe = "<p>hi</p><script>alert(1)</script><img src=x onerror=alert(1)>";

    private sealed class FakePlugins(bool enabled, bool throws = false) : IPlugins
    {
        public string? AskedAbout { get; private set; }

        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
        {
            AskedAbout = key;
            return throws ? throw new InvalidOperationException("the store is down") : Task.FromResult(enabled);
        }

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task WhenThePluginIsOn_HtmlIsSanitised_AndTheSwitchAskedAboutIsTheSanitiserKey()
    {
        var plugins = new FakePlugins(enabled: true);
        var sanitize = await new HtmlContentSanitizer(With(plugins)).GetAsync();

        var cleaned = sanitize(Unsafe);

        Assert.Contains("<p>hi</p>", cleaned);
        Assert.DoesNotContain("script", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("html-sanitizer", plugins.AskedAbout);
        Assert.Equal(HtmlSanitizerPlugin.Key, plugins.AskedAbout);
    }

    [Fact]
    public async Task WhenThePluginIsOff_HtmlPassesThroughAsWritten_AndNothingIsNull()
    {
        var sanitize = await new HtmlContentSanitizer(With(new FakePlugins(enabled: false))).GetAsync();

        Assert.Equal(Unsafe, sanitize(Unsafe));
        Assert.Equal(string.Empty, sanitize(null));
    }

    [Fact]
    public async Task WithNoPluginStateAtAll_HtmlIsStillSanitised()
    {
        var sanitize = await new HtmlContentSanitizer(new ServiceCollection().BuildServiceProvider()).GetAsync();

        Assert.DoesNotContain("script", sanitize(Unsafe), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IfTheSwitchCannotBeRead_HtmlIsStillSanitised()
    {
        var sanitize = await new HtmlContentSanitizer(With(new FakePlugins(enabled: false, throws: true))).GetAsync();

        Assert.DoesNotContain("script", sanitize(Unsafe), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddBlog_RegistersTheSeamAndDeclaresThePlugin_AndAHostCanReplaceTheSeam()
    {
        var services = new ServiceCollection();
        services.AddBlog();

        Assert.Contains(services, d => d.ServiceType == typeof(IHtmlContentSanitizer) && d.ImplementationType == typeof(HtmlContentSanitizer));
        var definition = Assert.Single(services.Where(d => d.ServiceType == typeof(PluginDefinition) && ((PluginDefinition)d.ImplementationInstance!).Key == "html-sanitizer"));
        Assert.Equal("HTML sanitising", ((PluginDefinition)definition.ImplementationInstance!).Title);

        // a later registration wins, so a developer's own sanitiser replaces the default without touching this library
        services.AddScoped<IHtmlContentSanitizer, ReplacementSanitizer>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<ReplacementSanitizer>(scope.ServiceProvider.GetRequiredService<IHtmlContentSanitizer>());
    }

    private sealed class ReplacementSanitizer : IHtmlContentSanitizer
    {
        public Task<Func<string?, string>> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<Func<string?, string>>(html => html ?? string.Empty);
    }
}
