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

        public Dictionary<string, Dictionary<string, string>> Proposals { get; } = new() { ["fr"] = new() { ["save"] = "Enregistrer" } };

        public Task ProposeAsync(string language, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken = default)
        {
            if (language == "bad")
            {
                throw new BusinessRuleException("A language code is two or three letters.");
            }

            Proposals[language] = new Dictionary<string, string>(labels);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LanguageSummary>> ListProposalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LanguageSummary>>(Proposals.Select(p => new LanguageSummary(p.Key, p.Value.Count)).ToList());

        public Task<IReadOnlyDictionary<string, string>?> GetProposalAsync(string language, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(Proposals.TryGetValue(language, out var table) ? table : null);

        public Task<bool> ApplyProposalAsync(string language, CancellationToken cancellationToken = default)
        {
            if (!Proposals.Remove(language, out var table))
            {
                return Task.FromResult(false);
            }

            Stored[language] = table;
            return Task.FromResult(true);
        }

        public Task<bool> DiscardProposalAsync(string language, CancellationToken cancellationToken = default) => Task.FromResult(Proposals.Remove(language));
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

    [Fact]
    public async Task TheScreen_ListsReadsAppliesAndDiscardsProposals()
    {
        var files = new FakeFiles();
        var controller = new LanguagesController(files, new FakeCatalog());

        Assert.Equal("fr", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<LanguageSummary>>(Assert.IsType<OkObjectResult>((await controller.Proposals(CancellationToken.None)).Result).Value)).Language);
        Assert.IsType<OkObjectResult>(await controller.ReadProposal("fr", CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.ReadProposal("xx", CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.ApplyProposal("fr", CancellationToken.None));
        Assert.Equal("Enregistrer", files.Stored["fr"]["save"]);
        Assert.IsType<NotFoundResult>(await controller.ApplyProposal("fr", CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.DiscardProposal("fr", CancellationToken.None));
    }

    [Fact]
    public async Task ClaudesCatalog_IsTheServerTextsPlusTheStoredEnglishTable_AndSaysWhenEnglishWasNeverSaved()
    {
        var files = new FakeFiles();
        files.Stored["en"] = new() { ["save"] = "Save", ["menu.posts"] = "Posts (stored)" };
        var controller = new AuthorLanguagesController(files, new FakeCatalog());

        var answer = Assert.IsType<LanguageCatalog>(Assert.IsType<OkObjectResult>((await controller.Catalog(CancellationToken.None)).Result).Value);

        Assert.Equal("Save", answer.Keys["save"]);
        Assert.Equal("Posts (stored)", answer.Keys["menu.posts"]);   // the stored English table wins over the server default
        Assert.Null(answer.Note);

        files.Stored.Remove("en");
        var bare = Assert.IsType<LanguageCatalog>(Assert.IsType<OkObjectResult>((await controller.Catalog(CancellationToken.None)).Result).Value);
        Assert.Equal("Posts", bare.Keys["menu.posts"]);
        Assert.Contains("en", bare.Note);
    }

    [Fact]
    public async Task AnAuthorsFile_BecomesAProposalOnly_NeverTheServedTable()
    {
        var files = new FakeFiles();
        var controller = new AuthorLanguagesController(files, new FakeCatalog());

        Assert.IsType<NoContentResult>(await controller.Propose("it", new LanguageBody(new Dictionary<string, string> { ["save"] = "Salva" }), CancellationToken.None));

        Assert.Equal("Salva", files.Proposals["it"]["save"]);
        Assert.False(files.Stored.ContainsKey("it"));
        Assert.IsNotType<NoContentResult>(await controller.Propose("bad", new LanguageBody(new Dictionary<string, string> { ["save"] = "a" }), CancellationToken.None));
    }

    [Fact]
    public void TheAuthorApi_NeedsTheDraftScope_AndHasNoRouteThatAppliesAFile()
    {
        var author = typeof(AuthorLanguagesController);
        Assert.Contains(author.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "Scope:content.draft");
        Assert.Contains(author.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/author/languages");
        Assert.DoesNotContain(author.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.Name.Contains("Apply") || m.Name.Contains("Save"));
        Assert.All(author.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));
    }
}
