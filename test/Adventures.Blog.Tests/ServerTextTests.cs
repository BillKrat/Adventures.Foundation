using Adventures.Data.NQuad;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// Server-side text (row 13, stage 2b): the words the server itself produces (menu names, action names) go through the standard IStringLocalizer. The table
/// is the tenant's language file for the site language, read once per request; an English default from the catalog covers a key the file leaves out, and the
/// key itself covers a key nobody declared, so a screen never shows nothing.
/// </summary>
public sealed class ServerTextTests : IAsyncLifetime
{
    private sealed class FakeSite(string language) : ISiteSettings
    {
        public Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicSiteSettings("T", string.Empty, 10, Language: language));

        public Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
    }

    private IServiceProvider _services = null!;
    private string _siteLanguage = "de";

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-026-language-files.nq"));
        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, LanguageConstants.SchemaIri), LanguageConstants.BaseIri, LanguageConstants.TypeIri, LanguageConstants.DefaultGraph, s => new LanguageFile(s));
        services.AddScoped<IEntityBll<LanguageFile>>(p => new EntityBll<LanguageFile>(p.GetRequiredService<IEntityDal<LanguageFile>>()));
        services.AddScoped<ILanguageFiles, LanguageFiles>();
        services.AddScoped<ISiteSettings>(_ => new FakeSite(_siteLanguage));
        services.AddServerText();
        services.AddServerText("menu.dashboard", "Dashboard");
        services.AddServerText("menu.posts", "Posts");
        services.AddServerText("greet", "Hello {0}");
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<ILanguageFiles>().SaveAsync("de", new Dictionary<string, string> { ["menu.dashboard"] = "Übersicht", ["greet"] = "Hallo {0}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<IStringLocalizer> LocalizerAsync(IServiceScope scope)
    {
        await scope.ServiceProvider.GetRequiredService<IRequestLanguage>().LoadAsync();
        return scope.ServiceProvider.GetRequiredService<IStringLocalizer>();
    }

    [Fact]
    public async Task AKeyInTheSiteLanguageFile_IsServedFromIt_AMissingOneFallsBackToTheEnglishDefault()
    {
        using var scope = _services.CreateScope();
        var text = await LocalizerAsync(scope);

        Assert.Equal("Übersicht", text["menu.dashboard"].Value);
        Assert.False(text["menu.dashboard"].ResourceNotFound);
        Assert.Equal("Posts", text["menu.posts"].Value);   // not in the German file: English
    }

    [Fact]
    public async Task AKeyNobodyDeclared_ShowsTheKey_AndIsMarkedNotFound()
    {
        using var scope = _services.CreateScope();
        var text = await LocalizerAsync(scope);

        Assert.Equal("menu.nothing", text["menu.nothing"].Value);
        Assert.True(text["menu.nothing"].ResourceNotFound);
    }

    [Fact]
    public async Task Arguments_AreFilledIntoTheTranslatedSentence()
    {
        using var scope = _services.CreateScope();
        var text = await LocalizerAsync(scope);

        Assert.Equal("Hallo Anna", text["greet", "Anna"].Value);
    }

    [Fact]
    public async Task ASiteInEnglishOrWithNoFile_GetsTheEnglishDefaults()
    {
        _siteLanguage = "en";
        using var english = _services.CreateScope();
        Assert.Equal("Dashboard", (await LocalizerAsync(english))["menu.dashboard"].Value);

        _siteLanguage = "fr";
        using var french = _services.CreateScope();
        Assert.Equal("Dashboard", (await LocalizerAsync(french))["menu.dashboard"].Value);
    }

    [Fact]
    public async Task BeforeTheTableIsLoaded_TheLocalizerStillAnswers_InEnglish()
    {
        using var scope = _services.CreateScope();

        Assert.Equal("Dashboard", scope.ServiceProvider.GetRequiredService<IStringLocalizer>()["menu.dashboard"].Value);
    }

    [Fact]
    public async Task TheTypedLocalizer_AnswersTheSameTexts()
    {
        using var scope = _services.CreateScope();
        await LocalizerAsync(scope);

        Assert.Equal("Übersicht", scope.ServiceProvider.GetRequiredService<IStringLocalizer<ServerTextTests>>()["menu.dashboard"].Value);
    }

    [Fact]
    public async Task EveryDeclaredText_IsListedWithItsEnglishDefault_ForTheLanguageScreen()
    {
        using var scope = _services.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<IServerTextCatalog>().Defaults;

        Assert.Equal("Dashboard", catalog["menu.dashboard"]);
        Assert.Equal("Hello {0}", catalog["greet"]);
        Assert.Equal(3, catalog.Count);
        await Task.CompletedTask;
    }
}
