using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Collaboration.Tests;

/// <summary>
/// The settings a plug-in declares and an administrator customizes, stored per plug-in (and per tenant, through the tenant-scoped repository the host
/// registers): defaults until set, validated as a whole before anything is written, one row per setting, never a setting the plug-in did not declare.
/// </summary>
public sealed class PluginSettingsTests : IAsyncLifetime
{
    private static readonly PluginDefinition Sample = new(
        "sample", "Sample", "A plug-in with settings.", "1.0.0", 10,
        [
            new PluginSettingDefinition("Greeting", "Greeting", "Shown to visitors.", PluginSettingKind.Text, "hello", MaxLength: 20),
            new PluginSettingDefinition("Loud", "Loud", "Shout it.", PluginSettingKind.Bool, "false"),
            new PluginSettingDefinition("Limit", "Limit", "How many.", PluginSettingKind.Number, "5"),
            new PluginSettingDefinition("Tags", "Tags", "One per line.", PluginSettingKind.Lines, string.Empty),
        ]);

    private static readonly PluginDefinition Other = new("other", "Other", "Also has a Greeting.", "1.0.0", 11,
        [new PluginSettingDefinition("Greeting", "Greeting", "Its own.", PluginSettingKind.Text, "other-default")]);

    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-016-plugins.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-024-plugin-settings.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, PluginConstants.SchemaIri), PluginConstants.BaseIri, PluginConstants.TypeIri, "https://global-webnet.com/graph/collaboration", s => new Plugin(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, PluginConstants.SettingSchemaIri), PluginConstants.SettingBaseIri, PluginConstants.SettingTypeIri, "https://global-webnet.com/graph/collaboration", s => new PluginSetting(s));
        services.AddPlugins();
        services.AddPlugin(Sample);
        services.AddPlugin(Other);
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IPluginSettings Settings() => _services.CreateScope().ServiceProvider.GetRequiredService<IPluginSettings>();

    [Fact]
    public async Task UntilSet_EverySettingHasItsDeclaredDefault()
    {
        var values = await Settings().GetAsync("sample");

        Assert.Equal("hello", values["Greeting"]);
        Assert.Equal("false", values["Loud"]);
        Assert.Equal("5", values["Limit"]);
        Assert.Equal(string.Empty, values["Tags"]);
    }

    [Fact]
    public async Task ASetValue_IsKept_AndTheOthersStayAtTheirDefault()
    {
        await Settings().SetAsync("sample", new Dictionary<string, string?> { ["Greeting"] = "  hi there  ", ["Loud"] = "true" });

        var values = await Settings().GetAsync("sample");
        Assert.Equal("hi there", values["Greeting"]);
        Assert.Equal("true", values["Loud"]);
        Assert.Equal("5", values["Limit"]);
    }

    [Fact]
    public async Task SettingTheSameNameAgain_ReplacesIt_WithOneRow()
    {
        await Settings().SetAsync("sample", new Dictionary<string, string?> { ["Greeting"] = "one" });
        await Settings().SetAsync("sample", new Dictionary<string, string?> { ["Greeting"] = "two" });

        Assert.Equal("two", (await Settings().GetAsync("sample"))["Greeting"]);
        var rows = await _services.CreateScope().ServiceProvider.GetRequiredService<IEntityBll<PluginSetting>>().ListAsync();
        Assert.Single(rows, r => r.GetValue("PluginKey") as string == "sample" && r.GetValue("Name") as string == "Greeting");
    }

    [Fact]
    public async Task TwoPlugins_CanUseTheSameSettingName_WithoutSeeingEachOther()
    {
        await Settings().SetAsync("sample", new Dictionary<string, string?> { ["Greeting"] = "from sample" });

        Assert.Equal("other-default", (await Settings().GetAsync("other"))["Greeting"]);
        await Settings().SetAsync("other", new Dictionary<string, string?> { ["Greeting"] = "from other" });
        Assert.Equal("from sample", (await Settings().GetAsync("sample"))["Greeting"]);
    }

    [Theory]
    [InlineData("Loud", "maybe")]
    [InlineData("Limit", "many")]
    [InlineData("Greeting", "this greeting is far too long for its limit")]
    public async Task AValueThatDoesNotFitItsKind_IsRefused(string name, string value)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Settings().SetAsync("sample", new Dictionary<string, string?> { [name] = value }));
    }

    [Fact]
    public async Task OneBadValue_WritesNothing_NotEvenTheGoodOnes()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Settings().SetAsync("sample", new Dictionary<string, string?> { ["Greeting"] = "fine", ["Limit"] = "not a number" }));

        Assert.Equal("hello", (await Settings().GetAsync("sample"))["Greeting"]);
    }

    [Fact]
    public async Task ASettingThePluginDidNotDeclare_AndAPluginThatDoesNotExist_AreRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Settings().SetAsync("sample", new Dictionary<string, string?> { ["Bogus"] = "x" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Settings().SetAsync("nope", new Dictionary<string, string?> { ["Greeting"] = "x" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Settings().GetAsync("nope"));
    }

    [Fact]
    public async Task APluginsOwnValidator_CanRefuseAValue_WithItsOwnMessage()
    {
        var picky = new PluginDefinition("picky", "Picky", "d", "1.0.0", 12,
            [new PluginSettingDefinition("Word", "Word", "d", PluginSettingKind.Text, "ok", Validate: v => v == "no" ? "That word is not allowed." : null)]);
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(new InMemoryNQuadStore());
        services.AddPlugin(picky);
        services.AddScoped<IPluginSettings>(p => new PluginSettings(p.GetRequiredService<IEntityBll<PluginSetting>>(), p.GetRequiredService<EntitySchemaOf<PluginSetting>>(), p.GetServices<PluginDefinition>()));
        services.AddScoped(_ => _services.GetRequiredService<IEntityBll<PluginSetting>>());
        services.AddSingleton(_services.GetRequiredService<EntitySchemaOf<PluginSetting>>());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPluginSettings>().SetAsync("picky", new Dictionary<string, string?> { ["Word"] = "no" }));

        Assert.Equal("That word is not allowed.", ex.Message);
    }
}
