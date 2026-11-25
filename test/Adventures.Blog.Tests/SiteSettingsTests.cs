// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Adventures.Blog;

using Adventures.Entities;

namespace Adventures.Blog.Tests;

/// <summary>The site settings: the seed ships four defaults, an administrator edits them as ordinary rows, and a bad value never breaks the site.</summary>
public sealed class SiteSettingsTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IEntityPresenter<SiteSetting> _presenter = null!;
    private ISiteSettings _settings = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-011-site-settings.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Site.SettingSchemaIri), BlogConstants.Site.SettingBaseIri, BlogConstants.Site.SettingTypeIri, BlogConstants.Site.DefaultGraph, s => new SiteSetting(s));
        services.AddScoped<IEntityBll<SiteSetting>>(p => new EntityBll<SiteSetting>(new EntityDal<SiteSetting>(p.GetRequiredService<IEntityRepository<SiteSetting>>())));
        services.AddScoped<IEntityPresenter<SiteSetting>, SiteSettingPresenter>();
        services.AddScoped<ISiteSettings, SiteSettings>();
        _services = services.BuildServiceProvider();
        _presenter = _services.GetRequiredService<IEntityPresenter<SiteSetting>>();
        _settings = _services.GetRequiredService<ISiteSettings>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static EntityDataModel Row(string name, string? value) => new(string.Empty, new Dictionary<string, string?> { ["Name"] = name, ["Value"] = value });

    [Fact]
    public async Task TheSeedShipsTheFourDefaults_AndTheScreenAddsTheOthers()
    {
        var list = await _presenter.ListAsync();

        Assert.Equal(["PostsPerPage", "SiteTitle", "Tagline", "TimeZone"], list.Select(r => r.Values["Name"]).Order());
        Assert.True((await _settings.GetPublicAsync()).CommentsEnabled);   // no row yet: on
        var grid = await _presenter.GetGridAsync(new EntityGridQuery());
        Assert.Contains(grid.Rows, r => r.Values["Name"] == "CommentsEnabled");
        var site = await _settings.GetPublicAsync();
        Assert.Equal("AI Research Blog", site.Title);
        Assert.Equal(10, site.PostsPerPage);
        Assert.Equal("UTC", await _settings.GetLegacyTimeZoneAsync());
    }

    [Fact]
    public async Task AnEditedTitleAndPageSize_AreReadBack_AndTheSizeIsKeptInRange()
    {
        var title = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "SiteTitle");
        await _presenter.UpdateAsync(title.EntityId, Row("SiteTitle", "  Adventures on the Edge  "));
        var size = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "PostsPerPage");

        await _presenter.UpdateAsync(size.EntityId, Row("PostsPerPage", "500"));
        Assert.Equal(50, (await _settings.GetPublicAsync()).PostsPerPage);
        await _presenter.UpdateAsync(size.EntityId, Row("PostsPerPage", "0"));
        Assert.Equal(1, (await _settings.GetPublicAsync()).PostsPerPage);
        await _presenter.UpdateAsync(size.EntityId, Row("PostsPerPage", "many"));

        var site = await _settings.GetPublicAsync();
        Assert.Equal("Adventures on the Edge", site.Title);
        Assert.Equal(10, site.PostsPerPage);   // not a number: the default
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData(" False ", false)]
    [InlineData("true", true)]
    [InlineData("anything else", true)]
    [InlineData("", true)]
    public async Task CommentsAreOnUnlessTheSettingSaysFalse(string value, bool expected)
    {
        await _presenter.GetGridAsync(new EntityGridQuery());   // the screen creates the row
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "CommentsEnabled");
        await _presenter.UpdateAsync(row.EntityId, Row("CommentsEnabled", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).CommentsEnabled);
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData(" LIGHT ", "light")]
    [InlineData("dark", "dark")]
    [InlineData("Dark", "dark")]
    [InlineData("purple", "dark")]
    [InlineData("", "dark")]
    public async Task TheColourModeIsDarkUnlessTheSettingSaysLight(string value, string expected)
    {
        Assert.Equal("dark", (await _settings.GetPublicAsync()).ColorMode);   // no row yet: dark, as the site always started
        await _presenter.GetGridAsync(new EntityGridQuery());                   // the screen creates the row
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "ColorMode");
        Assert.Equal("dark", row.Values["Value"]);
        await _presenter.UpdateAsync(row.EntityId, Row("ColorMode", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).ColorMode);
    }

    [Theory]
    [InlineData("violet", "violet")]
    [InlineData(" Rose ", "rose")]
    [InlineData("azure", "azure")]
    [InlineData("hot-pink", "azure")]
    [InlineData("<script>", "azure")]
    [InlineData("", "azure")]
    public async Task TheThemeIsAzureUnlessTheSettingNamesAMaterialPalette(string value, string expected)
    {
        Assert.Equal("azure", (await _settings.GetPublicAsync()).Theme);   // no row yet: azure, as the site always looked
        await _presenter.GetGridAsync(new EntityGridQuery());               // the screen creates the row
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "Theme");
        Assert.Equal("azure", row.Values["Value"]);
        await _presenter.UpdateAsync(row.EntityId, Row("Theme", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).Theme);
    }

    [Fact]
    public void TheThemeNames_AreTheTwelveMaterialPalettes() =>
        Assert.Equal(["azure", "blue", "chartreuse", "cyan", "green", "magenta", "orange", "red", "rose", "spring-green", "violet", "yellow"], SiteThemes.Names.Order());

    [Theory]
    [InlineData("de", "de")]
    [InlineData(" PT-br ", "pt-br")]
    [InlineData("klingon speak", "en")]
    [InlineData("", "en")]
    public async Task TheSiteLanguageIsEnglishUnlessTheSettingNamesALanguageCode(string value, string expected)
    {
        Assert.Equal("en", (await _settings.GetPublicAsync()).Language);   // no row yet: English
        await _presenter.GetGridAsync(new EntityGridQuery());
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "Language");
        Assert.Equal("en", row.Values["Value"]);
        await _presenter.UpdateAsync(row.EntityId, Row("Language", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).Language);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData(" FALSE ", false)]
    [InlineData("true", true)]
    [InlineData("no", true)]
    [InlineData("0", true)]
    [InlineData("", true)]
    public async Task CommentsAreModeratedUnlessTheSettingSaysExactlyFalse_ATypoNeverTurnsModerationOff(string value, bool expected)
    {
        Assert.True((await _settings.GetPublicAsync()).CommentsModerate);   // no row yet: moderated
        await _presenter.GetGridAsync(new EntityGridQuery());
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "CommentsModerate");
        Assert.Equal("true", row.Values["Value"]);
        await _presenter.UpdateAsync(row.EntityId, Row("CommentsModerate", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).CommentsModerate);
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("many", 0)]
    [InlineData("99999", 3650)]
    public async Task CommentsCloseAfterTheDaysSet_ZeroNeverAndAnythingOddMeansNever(string value, int expected)
    {
        Assert.Equal(0, (await _settings.GetPublicAsync()).CommentsCloseAfterDays);   // no row yet: never
        await _presenter.GetGridAsync(new EntityGridQuery());
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "CommentsCloseAfterDays");
        await _presenter.UpdateAsync(row.EntityId, Row("CommentsCloseAfterDays", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).CommentsCloseAfterDays);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("100", 100)]
    [InlineData("500", 100)]
    [InlineData("0", 20)]
    [InlineData("lots", 20)]
    public async Task TheFeedListsTheItemsSet_BetweenOneAndOneHundred_TwentyWhenUnsetOrOdd(string value, int expected)
    {
        Assert.Equal(20, (await _settings.GetPublicAsync()).FeedItems);
        await _presenter.GetGridAsync(new EntityGridQuery());
        var row = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "FeedItems");
        await _presenter.UpdateAsync(row.EntityId, Row("FeedItems", value));

        Assert.Equal(expected, (await _settings.GetPublicAsync()).FeedItems);
    }

    [Fact]
    public async Task OnlyTheValueIsEditable_ASettingCannotBeRenamed_AndNoneCanBeCreatedFromTheForm()
    {
        var title = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "SiteTitle");

        var rename = await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(() => _presenter.UpdateAsync(title.EntityId, Row("Titel", "x")));
        Assert.Contains("Name", rename.Errors.Keys);
        await _presenter.UpdateAsync(title.EntityId, Row("SiteTitle", "Adventures"));   // the same name with a new value is fine

        Assert.Equal("Adventures", (await _settings.GetPublicAsync()).Title);
        Assert.Null(await _presenter.GetNewFormAsync());
        var form = await _presenter.GetFormAsync(title.EntityId);
        Assert.False(form!.Permissions!.CanDelete);
        Assert.True(form.Schema.Fields.Single(f => f.Name == "Name").IsReadOnly);
    }

    [Fact]
    public async Task ABlankTitleOrAnUnknownTimeZone_FallsBackToTheDefault()
    {
        var title = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "SiteTitle");
        await _presenter.UpdateAsync(title.EntityId, Row("SiteTitle", "   "));
        var zone = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "TimeZone");
        await _presenter.UpdateAsync(zone.EntityId, Row("TimeZone", "Mars/Olympus"));

        Assert.Equal("AI Research Blog", (await _settings.GetPublicAsync()).Title);
        Assert.Equal("UTC", await _settings.GetLegacyTimeZoneAsync());
    }

    [Fact]
    public async Task TheSettingsScreen_CreatesAnyKnownSettingTheTenantLacks_WithItsDefault()
    {
        // a seed writes outside every tenant's graph, so a setting added by a later seed (or a new tenant) has no row until the screen asks
        await _presenter.GetGridAsync(new EntityGridQuery());
        var comments = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "CommentsEnabled");
        await _presenter.DeleteAsync(comments.EntityId);
        var perPage = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "PostsPerPage");
        await _presenter.UpdateAsync(perPage.EntityId, Row("PostsPerPage", "25"));
        Assert.DoesNotContain(await _presenter.ListAsync(), r => r.Values["Name"] == "CommentsEnabled");

        var grid = await _presenter.GetGridAsync(new EntityGridQuery());

        var recreated = Assert.Single(grid.Rows, r => r.Values["Name"] == "CommentsEnabled");
        Assert.Equal("true", recreated.Values["Value"]);
        Assert.NotEmpty(recreated.Values["Description"] ?? string.Empty);
        Assert.Equal(12, grid.Rows.Count);                                         // nothing duplicated
        Assert.Equal(25, (await _settings.GetPublicAsync()).PostsPerPage);        // an edited setting is left alone
        await _presenter.GetGridAsync(new EntityGridQuery());
        Assert.Equal(12, (await _presenter.ListAsync()).Count);                    // and asking again creates nothing more
    }

    [Fact]
    public async Task ABrandNewTenantWithNoSettingsAtAll_GetsEveryKnownSettingOnFirstOpen()
    {
        foreach (var row in await _presenter.ListAsync())
        {
            await _presenter.DeleteAsync(row.EntityId);
        }

        var grid = await _presenter.GetGridAsync(new EntityGridQuery());

        Assert.Equal(["ColorMode", "CommentsCloseAfterDays", "CommentsEnabled", "CommentsModerate", "FeedItems", "Language", "PostsPerPage", "SiteTitle", "Tagline", "Theme", "TimeZone", "TrackingScriptUrl"], grid.Rows.Select(r => r.Values["Name"]).Order());
        Assert.Equal("AI Research Blog", (await _settings.GetPublicAsync()).Title);
    }

    [Fact]
    public async Task ADeletedSetting_FallsBackToItsDefault()
    {
        var perPage = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "PostsPerPage");

        await _presenter.DeleteAsync(perPage.EntityId);

        Assert.Equal(10, (await _settings.GetPublicAsync()).PostsPerPage);
    }
}
