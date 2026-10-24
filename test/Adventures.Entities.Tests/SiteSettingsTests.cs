// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

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
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Site.SettingSchemaIri), EntityConstants.Site.SettingBaseIri, EntityConstants.Site.SettingTypeIri, EntityConstants.Site.DefaultGraph, s => new SiteSetting(s));
        services.AddScoped<IEntityBll<SiteSetting>>(p => new EntityBll<SiteSetting>(p.GetRequiredService<IEntityRepository<SiteSetting>>()));
        services.AddScoped<IEntityPresenter<SiteSetting>, SiteSettingPresenter>();
        services.AddScoped<ISiteSettings, SiteSettings>();
        _services = services.BuildServiceProvider();
        _presenter = _services.GetRequiredService<IEntityPresenter<SiteSetting>>();
        _settings = _services.GetRequiredService<ISiteSettings>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static EntityDataModel Row(string name, string? value) => new(string.Empty, new Dictionary<string, string?> { ["Name"] = name, ["Value"] = value });

    [Fact]
    public async Task TheSeedShipsTheFourDefaults()
    {
        var list = await _presenter.ListAsync();

        Assert.Equal(["PostsPerPage", "SiteTitle", "Tagline", "TimeZone"], list.Select(r => r.Values["Name"]).Order());
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
    public async Task ADeletedSetting_FallsBackToItsDefault()
    {
        var perPage = (await _presenter.ListAsync()).Single(r => r.Values["Name"] == "PostsPerPage");

        await _presenter.DeleteAsync(perPage.EntityId);

        Assert.Equal(10, (await _settings.GetPublicAsync()).PostsPerPage);
    }
}
