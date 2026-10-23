using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>The page rules BlogEngine kept in its editor: tree without cycles, one front page, slug and date defaults, blank form defaults, parent shown by title.</summary>
public sealed class PageBllTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IPageBll _pages = null!;
    private IEntityPresenter<Page> _presenter = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-006-content.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, EntityConstants.Content.PageSchemaIri), EntityConstants.Content.PageBaseIri, EntityConstants.Content.PageTypeIri,
            EntityConstants.Content.DefaultGraph, s => new Page(s));
        services.AddNQuadRelations();
        services.AddScoped<IPageBll, PageBll>();
        services.AddScoped<IEntityPresenter<Page>>(p => new EntityPresenter<Page>(
            p.GetRequiredService<IPageBll>(), p.GetRequiredService<EntitySchemaOf<Page>>().Schema, s => new Page(s), p.GetService<IEntityRelationProvider>()));
        _services = services.BuildServiceProvider();
        _pages = _services.GetRequiredService<IPageBll>();
        _presenter = _services.GetRequiredService<IEntityPresenter<Page>>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> CreateAsync(string title, string? parent = null, bool front = false)
    {
        var values = new Dictionary<string, string?> { ["Title"] = title };
        if (parent is not null)
        {
            values["Parent"] = parent;
        }

        if (front)
        {
            values["IsFrontPage"] = "true";
        }

        return (await _presenter.CreateAsync(new EntityDataModel(string.Empty, values))).Entity.EntityId;
    }

    [Fact]
    public async Task ANewPage_GetsASlugFromItsTitle_AndADatePublished()
    {
        var id = await CreateAsync("Direct Corpus Interaction: Part 1!");

        var page = (await _presenter.GetFormAsync(id))!.Entity.Values;

        Assert.Equal("direct-corpus-interaction-part-1", page["Slug"]);
        Assert.False(string.IsNullOrEmpty(page["DatePublished"]));
        Assert.Equal("direct-corpus-interaction", PageBll.Slugify("  Direct   Corpus -- Interaction "));
    }

    [Fact]
    public async Task ATitleIsRequired()
    {
        await Assert.ThrowsAsync<InputValidationException>(() => _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Slug"] = "x" })));
    }

    [Fact]
    public async Task APageCannotBeItsOwnParent_OrMoveUnderItsOwnDescendant()
    {
        var root = await CreateAsync("Root");
        var child = await CreateAsync("Child", parent: root);
        var grandchild = await CreateAsync("Grandchild", parent: child);

        var self = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _presenter.UpdateAsync(root, new EntityDataModel(root, new Dictionary<string, string?> { ["Parent"] = root })));
        Assert.Contains("own parent", self.Message);

        var loop = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _presenter.UpdateAsync(root, new EntityDataModel(root, new Dictionary<string, string?> { ["Parent"] = grandchild })));
        Assert.Contains("own sub-pages", loop.Message);

        // moving a leaf elsewhere is fine
        await _presenter.UpdateAsync(grandchild, new EntityDataModel(grandchild, new Dictionary<string, string?> { ["Parent"] = root }));
    }

    [Fact]
    public async Task OnlyOnePageIsTheFrontPage_TheLastOneSetWins()
    {
        var first = await CreateAsync("Home", front: true);
        var second = await CreateAsync("Welcome", front: true);

        Assert.Equal("false", (await _presenter.GetFormAsync(first))!.Entity.Values["IsFrontPage"]);
        Assert.Equal("true", (await _presenter.GetFormAsync(second))!.Entity.Values["IsFrontPage"]);

        await _presenter.UpdateAsync(first, new EntityDataModel(first, new Dictionary<string, string?> { ["IsFrontPage"] = "true" }));
        Assert.Equal("true", (await _presenter.GetFormAsync(first))!.Entity.Values["IsFrontPage"]);
        Assert.Equal("false", (await _presenter.GetFormAsync(second))!.Entity.Values["IsFrontPage"]);
    }

    [Fact]
    public async Task TheBlankFormStartsWithSchemaDefaults_AndTheParentIsShownByTitle()
    {
        var blank = await _presenter.GetNewFormAsync();

        Assert.Equal(string.Empty, blank!.Entity.EntityId);
        Assert.Equal("true", blank.Entity.Values["ShowInList"]);
        Assert.Equal("false", blank.Entity.Values["IsPublished"]);
        Assert.Equal("0", blank.Entity.Values["SortOrder"]);
        Assert.Equal(["Id", "Title"], blank.Schema.Fields.Take(2).Select(f => f.Name));
        Assert.Contains(blank.Schema.Fields, f => f.Name == "Parent" && f.RefSchema == EntityConstants.Content.PageSchemaIri);
        Assert.Contains(blank.Schema.Fields, f => f.Name == "Content" && f.Type == "Markdown" && !f.IsListVisible);

        var root = await CreateAsync("Article");
        var child = await CreateAsync("Angular", parent: root);
        var form = await _presenter.GetFormAsync(child);
        Assert.Equal("Article", form!.Entity.Display!["Parent"]);

        var grid = await _presenter.GetGridAsync(new EntityGridQuery(Sort: "Title"));
        Assert.Equal(["Angular", "Article"], grid.Rows.Select(r => r.Values["Title"]));
        Assert.Equal("Article", grid.Rows[0].Display!["Parent"]);
    }

    [Fact]
    public async Task TwoPagesNeverShareAnAddress()
    {
        var first = await CreateAsync("About");
        var second = await CreateAsync("About");

        Assert.Equal("about", (await _presenter.GetFormAsync(first))!.Entity.Values["Slug"]);
        Assert.Equal("about-2", (await _presenter.GetFormAsync(second))!.Entity.Values["Slug"]);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Title"] = "Team", ["Slug"] = "ABOUT" })));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _presenter.UpdateAsync(second, new EntityDataModel(second, new Dictionary<string, string?> { ["Slug"] = "about" })));
    }

    [Fact]
    public async Task ParentChoicesExcludeThePageBeingEdited()
    {
        var root = await CreateAsync("Article");
        await CreateAsync("Angular", parent: root);
        var provider = _services.GetRequiredService<IEntityRelationProvider>();

        var all = await provider.GetOptionsAsync(EntityConstants.Content.PageSchemaIri);
        var withoutSelf = await provider.GetOptionsAsync(EntityConstants.Content.PageSchemaIri, excludeId: root);

        Assert.Equal(["Angular", "Article"], all.Select(o => o.Text));
        Assert.Equal(["Angular"], withoutSelf.Select(o => o.Text));
    }
}
