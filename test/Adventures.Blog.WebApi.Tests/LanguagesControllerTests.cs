using System.Reflection;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>The language files' API: the screen's list, read, save and remove (site-settings right) and the public read of the table of the site language (anonymous).</summary>
public sealed class LanguagesControllerTests
{
    private sealed class FakeFiles : ILanguageFiles
    {
        public Dictionary<string, Dictionary<string, string>> Stored { get; } = new() { ["de"] = new() { ["save"] = "Speichern" } };

        public Task<IReadOnlyDictionary<string, string>> GetAsync(string language, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Stored.TryGetValue(language, out var table) ? table : new Dictionary<string, string>());

        public Task<IReadOnlyList<LanguageSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LanguageSummary>>(Stored.Select(s => new LanguageSummary(s.Key, s.Value.Count)).ToList());

        public Task SaveAsync(string language, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken = default)
        {
            if (language == "bad")
            {
                throw new BusinessRuleException("A language code is two or three letters.");
            }

            Stored[language] = new Dictionary<string, string>(labels);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string language, CancellationToken cancellationToken = default) => Task.FromResult(Stored.Remove(language));
    }

    private sealed class FakeCatalog : IServerTextCatalog
    {
        public IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string> { ["menu.posts"] = "Posts" };
    }

    private sealed class FakeSite(string language) : ISiteSettings
    {
        public Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicSiteSettings("T", string.Empty, 10, Language: language));

        public Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
    }

    [Fact]
    public async Task ThePublicTable_IsTheTableOfTheSiteLanguage()
    {
        var controller = new PublicLabelsController(new FakeSite("de"), new FakeFiles());

        var answer = Assert.IsType<PublicLabels>(Assert.IsType<OkObjectResult>((await controller.Get(CancellationToken.None)).Result).Value);

        Assert.Equal("de", answer.Language);
        Assert.Equal("Speichern", answer.Labels["save"]);
    }

    [Fact]
    public async Task ASiteLanguageWithNoFile_ServesAnEmptyTable_SoTheClientKeepsEnglish()
    {
        var controller = new PublicLabelsController(new FakeSite("fr"), new FakeFiles());

        var answer = Assert.IsType<PublicLabels>(Assert.IsType<OkObjectResult>((await controller.Get(CancellationToken.None)).Result).Value);

        Assert.Equal("fr", answer.Language);
        Assert.Empty(answer.Labels);
    }

    [Fact]
    public async Task TheScreen_ListsReadsSavesAndRemoves()
    {
        var files = new FakeFiles();
        var controller = new LanguagesController(files, new FakeCatalog());

        Assert.Equal("de", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<LanguageSummary>>(Assert.IsType<OkObjectResult>((await controller.List(CancellationToken.None)).Result).Value)).Language);
        Assert.IsType<OkObjectResult>(await controller.Read("de", CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Save("fr", new LanguageBody(new Dictionary<string, string> { ["save"] = "Enregistrer" }), CancellationToken.None));
        Assert.Equal("Enregistrer", files.Stored["fr"]["save"]);
        Assert.IsType<NoContentResult>(await controller.Remove("fr", CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Remove("fr", CancellationToken.None));
    }

    [Fact]
    public void TheCatalog_ListsTheServerTextsWithTheirEnglishDefaults()
    {
        var answer = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(Assert.IsType<OkObjectResult>(new LanguagesController(new FakeFiles(), new FakeCatalog()).Catalog().Result).Value);

        Assert.Equal("Posts", answer["menu.posts"]);
    }

    [Fact]
    public async Task ReadingAnUnknownLanguage_IsNotFound_AndABadSaveIsARefusalNotACrash()
    {
        var controller = new LanguagesController(new FakeFiles(), new FakeCatalog());

        Assert.IsType<NotFoundResult>(await controller.Read("xx", CancellationToken.None));
        var refused = await controller.Save("bad", new LanguageBody(new Dictionary<string, string> { ["save"] = "a" }), CancellationToken.None);
        Assert.IsNotType<NoContentResult>(refused);
        Assert.IsNotType<OkResult>(refused);
    }

    [Fact]
    public void TheAdminApi_NeedsTheSettingsRight_AndTheLabelReadIsAnonymousOnlyUnderApiPublic()
    {
        var admin = typeof(LanguagesController);
        Assert.Contains(admin.GetCustomAttributes<RequireRightAttribute>(), a => a.Right == Rights.SettingsManage);
        Assert.Contains(admin.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/languages");
        Assert.All(admin.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));

        var open = typeof(PublicLabelsController);
        Assert.NotEmpty(open.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(open.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/public/labels");
    }
}
