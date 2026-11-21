using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// The sanitiser's Customize settings: an owner may widen the allow-list (extra tags, attributes and URL schemes) but never past the line that
/// separates "allowed markup" from "switch the plug-in off": scripts, loaders, event handlers and script-capable addresses stay refused, when the
/// value is saved and again when it is used.
/// </summary>
public sealed class HtmlSanitizerSettingsTests
{
    private static HtmlAllowList Allow(string tags = "", string attributes = "", string schemes = "http,https,mailto") =>
        HtmlAllowList.From(new Dictionary<string, string>
        {
            [HtmlSanitizerPlugin.ExtraAllowedTags] = tags,
            [HtmlSanitizerPlugin.ExtraAllowedAttributes] = attributes,
            [HtmlSanitizerPlugin.AllowedUrlSchemes] = schemes,
        });

    [Fact]
    public void TheDefaultAllowList_IsTodaysRules()
    {
        var cleaned = HtmlContent.Sanitize("<p data-note=\"n\">a</p><x-widget>w</x-widget><a href=\"tel:123\">call</a>", HtmlAllowList.Default);

        Assert.DoesNotContain("x-widget", cleaned);
        Assert.DoesNotContain("data-note", cleaned);
        Assert.DoesNotContain("tel:", cleaned);
    }

    [Fact]
    public void AnExtraTag_IsKept_WhenTheOwnerAllowsIt()
    {
        var cleaned = HtmlContent.Sanitize("<x-widget>w</x-widget>", Allow(tags: "x-widget"));

        Assert.Contains("<x-widget>w</x-widget>", cleaned);
    }

    [Fact]
    public void AnExtraAttribute_IsKept_WhenTheOwnerAllowsIt()
    {
        var cleaned = HtmlContent.Sanitize("<p data-note=\"n\">a</p>", Allow(attributes: "data-note"));

        Assert.Contains("data-note=\"n\"", cleaned);
    }

    [Fact]
    public void AnExtraUrlScheme_IsKept_WhenTheOwnerAllowsIt()
    {
        var cleaned = HtmlContent.Sanitize("<a href=\"tel:123\">call</a>", Allow(schemes: "http,https,mailto,tel"));

        Assert.Contains("tel:123", cleaned);
    }

    [Fact]
    public void TheRulesThatAreNotCustomizable_StillApply_WhateverIsAllowed()
    {
        var cleaned = HtmlContent.Sanitize("<p onclick=\"x()\">a</p><script>x()</script><a href=\"javascript:x()\">j</a>", Allow(tags: "x-widget", attributes: "data-note"));

        Assert.DoesNotContain("onclick", cleaned);
        Assert.DoesNotContain("script", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("SCRIPT, b")]
    [InlineData("object")]
    [InlineData("embed")]
    [InlineData("style")]
    [InlineData("meta")]
    [InlineData("base")]
    public void AScriptOrLoaderTag_CannotBeAllowed_AtSaveOrAtUse(string tags)
    {
        Assert.NotNull(HtmlAllowList.ValidateTags(tags));
        // even if it got into the store another way, it is dropped when the list is built
        Assert.DoesNotContain(Allow(tags: tags).Tags, t => t is "script" or "object" or "embed" or "style" or "meta" or "base");
    }

    [Theory]
    [InlineData("onclick")]
    [InlineData("OnLoad")]
    [InlineData("srcdoc")]
    [InlineData("formaction")]
    public void AnEventOrScriptAttribute_CannotBeAllowed_AtSaveOrAtUse(string attributes)
    {
        Assert.NotNull(HtmlAllowList.ValidateAttributes(attributes));
        Assert.Empty(Allow(attributes: attributes).Attributes);
    }

    [Theory]
    [InlineData("javascript")]
    [InlineData("data")]
    [InlineData("http,vbscript")]
    [InlineData("file")]
    public void AScriptCapableScheme_CannotBeAllowed_AtSaveOrAtUse(string schemes)
    {
        Assert.NotNull(HtmlAllowList.ValidateSchemes(schemes));
        Assert.DoesNotContain(Allow(schemes: schemes).Schemes, s => s is "javascript" or "data" or "vbscript" or "file");
    }

    [Theory]
    [InlineData("x-widget, details\nsummary")]
    [InlineData("")]
    public void APlainList_Passes_WhetherCommaSpaceOrLineSeparated(string tags)
    {
        Assert.Null(HtmlAllowList.ValidateTags(tags));
    }

    [Theory]
    [InlineData("a b!")]
    [InlineData("<b>")]
    [InlineData("1abc")]
    public void ANameThatIsNotATagName_IsRefused(string tags)
    {
        Assert.NotNull(HtmlAllowList.ValidateTags(tags));
    }

    [Fact]
    public void TheSchemesMustNotBeEmpty_AndOnlyKnownSafeOnesAreAccepted()
    {
        Assert.NotNull(HtmlAllowList.ValidateSchemes(string.Empty));
        Assert.Null(HtmlAllowList.ValidateSchemes("http, https, mailto, tel, ftp"));
        Assert.NotNull(HtmlAllowList.ValidateSchemes("gopher"));
    }

    [Fact]
    public void ThePluginDeclaresTheThreeSettings_WithTheirDefaults_AndTheValidators()
    {
        var settings = HtmlSanitizerPlugin.Definition.Settings!;

        Assert.Equal([HtmlSanitizerPlugin.ExtraAllowedTags, HtmlSanitizerPlugin.ExtraAllowedAttributes, HtmlSanitizerPlugin.AllowedUrlSchemes], settings.Select(s => s.Name));
        Assert.Equal("http,https,mailto", settings.Single(s => s.Name == HtmlSanitizerPlugin.AllowedUrlSchemes).Default);
        Assert.All(settings, s => Assert.NotNull(s.Validate));
        Assert.NotNull(settings.Single(s => s.Name == HtmlSanitizerPlugin.ExtraAllowedTags).Validate!("script"));
    }

    private sealed class FakePlugins(bool enabled) : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(enabled);

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings(Dictionary<string, string>? values, bool throws = false) : IPluginSettings
    {
        public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) => [];

        public Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default) =>
            throws ? throw new InvalidOperationException("down") : Task.FromResult<IReadOnlyDictionary<string, string>>(values!);

        public Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> v, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static IServiceProvider With(IPluginSettings settings) =>
        new ServiceCollection().AddSingleton<IPlugins>(new FakePlugins(true)).AddSingleton(settings).BuildServiceProvider();

    [Fact]
    public async Task TheSanitiser_UsesTheStoredAllowList()
    {
        var sanitize = await new HtmlContentSanitizer(With(new FakeSettings(new() { [HtmlSanitizerPlugin.ExtraAllowedTags] = "x-widget", [HtmlSanitizerPlugin.ExtraAllowedAttributes] = "", [HtmlSanitizerPlugin.AllowedUrlSchemes] = "http,https,mailto" }))).GetAsync();

        Assert.Contains("<x-widget>w</x-widget>", sanitize("<x-widget>w</x-widget><script>x()</script>"));
        Assert.DoesNotContain("script", sanitize("<x-widget>w</x-widget><script>x()</script>"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IfTheSettingsCannotBeRead_TheDefaultRulesApply()
    {
        var sanitize = await new HtmlContentSanitizer(With(new FakeSettings(null, throws: true))).GetAsync();

        var cleaned = sanitize("<x-widget>w</x-widget><script>x()</script>");
        Assert.DoesNotContain("x-widget", cleaned);
        Assert.DoesNotContain("script", cleaned, StringComparison.OrdinalIgnoreCase);
    }
}
