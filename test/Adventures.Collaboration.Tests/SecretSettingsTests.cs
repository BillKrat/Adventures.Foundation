using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.MEF;
using Adventures.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Collaboration.Tests;

/// <summary>
/// A plug-in setting of kind Secret (a webhook address, an API token): encrypted before it is stored, never returned by the screen's read (only "set" or "not set"),
/// kept when the field is left empty, cleared only on request, and refused outright when no encryption key is configured, so it is never stored as plain text.
/// </summary>
public sealed class SecretSettingsTests
{
    private const string Key = "k1-0123456789abcdef0123456789abcdef-aaaa";

    private static readonly PluginDefinition Sample = new(
        "sample", "Sample", "A plug-in with a secret.", "1.0.0", 10,
        [
            new PluginSettingDefinition("Greeting", "Greeting", "Plain.", PluginSettingKind.Text, "hello"),
            new PluginSettingDefinition("Token", "Token", "An API token.", PluginSettingKind.Secret, string.Empty, MaxLength: 40,
                Validate: v => v.StartsWith("tok-", StringComparison.Ordinal) ? null : "A token starts with tok-."),
        ]);

    private static async Task<IServiceProvider> BuildAsync(bool withKey)
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
        var settings = new Dictionary<string, string?>();
        if (withKey)
        {
            settings["Encryption:CurrentKeyId"] = "k1";
            settings["Encryption:Keys:k1"] = Key;
        }

        services.AddFieldEncryption(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    private static IPluginSettings Settings(IServiceProvider services) => services.CreateScope().ServiceProvider.GetRequiredService<IPluginSettings>();

    private static async Task<string?> StoredValueAsync(IServiceProvider services)
    {
        var rows = await services.CreateScope().ServiceProvider.GetRequiredService<IEntityBll<PluginSetting>>().ListAsync();
        return rows.Single(r => r.GetValue("Name") as string == "Token").GetValue("Value") as string;
    }

    [Fact]
    public async Task ASecret_IsStoredEncrypted_AndTheServerReadsItBack()
    {
        var services = await BuildAsync(withKey: true);

        await Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-abc123" });

        var stored = await StoredValueAsync(services);
        Assert.StartsWith("enc1:k1:", stored);
        Assert.DoesNotContain("abc123", stored);
        Assert.Equal("tok-abc123", (await Settings(services).GetAsync("sample"))["Token"]);
    }

    [Fact]
    public async Task TheScreensRead_NeverCarriesTheSecret_OnlyThatItIsSet()
    {
        var services = await BuildAsync(withKey: true);
        var before = await Settings(services).GetForDisplayAsync("sample");
        Assert.Empty(before.SecretsSet);

        await Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-abc123", ["Greeting"] = "hi" });
        var after = await Settings(services).GetForDisplayAsync("sample");

        Assert.Equal(string.Empty, after.Values["Token"]);
        Assert.Equal("hi", after.Values["Greeting"]);
        Assert.Equal(["Token"], after.SecretsSet);
    }

    [Fact]
    public async Task AnEmptyField_KeepsTheSecret_SoSavingOtherSettingsDoesNotWipeIt()
    {
        var services = await BuildAsync(withKey: true);
        await Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-abc123" });

        await Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = string.Empty, ["Greeting"] = "changed" });

        Assert.Equal("tok-abc123", (await Settings(services).GetAsync("sample"))["Token"]);
    }

    [Fact]
    public async Task ASecret_IsClearedOnlyWhenAsked()
    {
        var services = await BuildAsync(withKey: true);
        await Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-abc123" });

        await Settings(services).SetAsync("sample", new Dictionary<string, string?>(), clear: ["Token"]);

        Assert.Equal(string.Empty, (await Settings(services).GetAsync("sample"))["Token"]);
        Assert.Empty((await Settings(services).GetForDisplayAsync("sample")).SecretsSet);
    }

    [Fact]
    public async Task WithNoEncryptionKey_ASecretIsRefused_AndNothingIsStored()
    {
        var services = await BuildAsync(withKey: false);

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-abc123", ["Greeting"] = "hi" }));

        Assert.Contains("Encryption", refusal.Message);
        Assert.DoesNotContain("abc123", refusal.Message);
        Assert.Equal("hello", (await Settings(services).GetAsync("sample"))["Greeting"]); // the plain value in the same save was not written either
        var rows = await services.CreateScope().ServiceProvider.GetRequiredService<IEntityBll<PluginSetting>>().ListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ThePlugInsOwnRuleAndTheLengthLimit_ApplyToTheSecret_BeforeItIsEncrypted()
    {
        var services = await BuildAsync(withKey: true);

        var rule = await Assert.ThrowsAsync<BusinessRuleException>(() => Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "nope" }));
        Assert.Equal("A token starts with tok-.", rule.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-" + new string('x', 60) }));
    }

    [Fact]
    public async Task APlainTextValueFoundInTheStore_IsNeverReturnedAsTheSecret()
    {
        var services = await BuildAsync(withKey: true);
        var scope = services.CreateScope().ServiceProvider;
        var row = new PluginSetting(scope.GetRequiredService<EntitySchemaOf<PluginSetting>>().Schema);
        row.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        row.Set("PluginKey", Guid.NewGuid().ToString(), "sample");
        row.Set("Name", Guid.NewGuid().ToString(), "Token");
        row.Set("Value", Guid.NewGuid().ToString(), "tok-written-by-hand");
        await scope.GetRequiredService<IEntityBll<PluginSetting>>().CreateAsync(row);

        Assert.Equal(string.Empty, (await Settings(services).GetAsync("sample"))["Token"]);
        Assert.Empty((await Settings(services).GetForDisplayAsync("sample")).SecretsSet);
    }

    [Fact]
    public async Task ASecretMovedToAnotherSetting_DoesNotDecrypt()
    {
        var services = await BuildAsync(withKey: true);
        await Settings(services).SetAsync("sample", new Dictionary<string, string?> { ["Token"] = "tok-abc123" });
        var stored = await StoredValueAsync(services);
        var encryptor = services.GetRequiredService<IFieldEncryptor>();

        Assert.Null(encryptor.TryDecrypt(stored!, "plugin-setting:sample:Greeting"));
        Assert.Null(encryptor.TryDecrypt(stored!, "plugin-setting:other:Token"));
    }
}
