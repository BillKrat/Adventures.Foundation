using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>Posts: always created as drafts, published only by the publish action, categories and tags as multi-valued pointers shown by name.</summary>
public sealed class PostBllTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IPostBll _bll = null!;
    private IEntityPresenter<Post> _posts = null!;
    private IEntityPresenter<Category> _categories = null!;
    private IEntityPresenter<Tag> _tags = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-006-content.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-007-post-taxonomy.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-008-post-revision.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        const string graph = EntityConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.PostSchemaIri), EntityConstants.Content.PostBaseIri, EntityConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.CategorySchemaIri), EntityConstants.Content.CategoryBaseIri, EntityConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.TagSchemaIri), EntityConstants.Content.TagBaseIri, EntityConstants.Content.TagTypeIri, graph, s => new Tag(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Schema.UserIri), EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, EntityConstants.User.DefaultGraph, s => new User(s));
        services.AddNQuadRelations();
        services.AddScoped<IPostBll, PostBll>();
        services.AddScoped<IEntityBll<Category>>(p => new EntityBll<Category>(p.GetRequiredService<IEntityRepository<Category>>()));
        services.AddScoped<IEntityBll<Tag>>(p => new EntityBll<Tag>(p.GetRequiredService<IEntityRepository<Tag>>()));
        services.AddScoped<IEntityPresenter<Post>, PostPresenter>();
        services.AddScoped<IEntityPresenter<Category>>(p => new EntityPresenter<Category>(
            p.GetRequiredService<IEntityBll<Category>>(), p.GetRequiredService<EntitySchemaOf<Category>>().Schema, s => new Category(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IEntityPresenter<Tag>>(p => new EntityPresenter<Tag>(
            p.GetRequiredService<IEntityBll<Tag>>(), p.GetRequiredService<EntitySchemaOf<Tag>>().Schema, s => new Tag(s), p.GetService<IEntityRelationProvider>()));
        _services = services.BuildServiceProvider();
        _bll = _services.GetRequiredService<IPostBll>();
        _posts = _services.GetRequiredService<IEntityPresenter<Post>>();
        _categories = _services.GetRequiredService<IEntityPresenter<Category>>();
        _tags = _services.GetRequiredService<IEntityPresenter<Tag>>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static EntityDataModel Data(params (string Key, string? Value)[] values) =>
        new(string.Empty, values.ToDictionary(v => v.Key, v => v.Value));

    private async Task<string> NewCategoryAsync(string name) => (await _categories.CreateAsync(Data(("Name", name)))).Entity.EntityId;

    private async Task<string> NewTagAsync(string name) => (await _tags.CreateAsync(Data(("Name", name)))).Entity.EntityId;

    [Fact]
    public async Task ANewPost_IsADraftWithASlugAndADate_AndTheFormCannotPublishIt()
    {
        var created = await _posts.CreateAsync(Data(("Title", "Direct Corpus Interaction: Part 1")));

        Assert.Equal("false", created.Entity.Values["IsPublished"]);
        Assert.Equal("direct-corpus-interaction-part-1", created.Entity.Values["Slug"]);
        Assert.False(string.IsNullOrEmpty(created.Entity.Values["DatePublished"]));

        var attempt = await Assert.ThrowsAsync<InputValidationException>(() => _posts.CreateAsync(Data(("Title", "Sneaky"), ("IsPublished", "true"))));
        Assert.Contains("set by the server", attempt.Errors["IsPublished"].Single());

        // the blank form posted back unchanged (IsPublished = the default, false) is fine
        await _posts.CreateAsync(Data(("Title", "Fine"), ("IsPublished", "false")));
    }

    [Fact]
    public async Task TheListShowsTheNewestPostFirst_UnlessTheCallerSorts()
    {
        await _posts.CreateAsync(Data(("Title", "Oldest")));
        await Task.Delay(20);
        await _posts.CreateAsync(Data(("Title", "Middle")));
        await Task.Delay(20);
        await _posts.CreateAsync(Data(("Title", "Newest")));

        var byDefault = await _posts.GetGridAsync(new EntityGridQuery());
        Assert.Equal(["Newest", "Middle", "Oldest"], byDefault.Rows.Select(r => r.Values["Title"]));
        Assert.Equal("DatePublished", byDefault.Paging!.Sort);
        Assert.True(byDefault.Paging.Descending);

        var byTitle = await _posts.GetGridAsync(new EntityGridQuery(Sort: "Title"));
        Assert.Equal(["Middle", "Newest", "Oldest"], byTitle.Rows.Select(r => r.Values["Title"]));
    }

    [Fact]
    public async Task OnlyThePublishActionChangesTheState_AndAnUpdateCannotFlipIt()
    {
        var id = (await _posts.CreateAsync(Data(("Title", "Draft")))).Entity.EntityId;

        var refused = await Assert.ThrowsAsync<InputValidationException>(() => _posts.UpdateAsync(id, Data(("IsPublished", "true"))));
        Assert.Equal("Is published cannot be changed.", refused.Errors["IsPublished"].Single());

        Assert.True(await _bll.PublishAsync(id));
        Assert.Equal("true", (await _posts.GetFormAsync(id))!.Entity.Values["IsPublished"]);
        Assert.True(await _bll.UnpublishAsync(id));
        Assert.Equal("false", (await _posts.GetFormAsync(id))!.Entity.Values["IsPublished"]);
        Assert.False(await _bll.PublishAsync("no-such-post"));
    }

    [Fact]
    public async Task CategoriesAndTags_AreMultiValuedPointers_ShownByNameAndReplacedAsASet()
    {
        var angular = await NewCategoryAsync("Angular");
        var cli = await NewCategoryAsync("CLI");
        var ai = await NewTagAsync("AI");
        var id = (await _posts.CreateAsync(Data(("Title", "Post"), ("Categories", $"{angular},{cli}"), ("Tags", ai)))).Entity.EntityId;

        var form = await _posts.GetFormAsync(id);
        Assert.Equal(new[] { angular, cli }.Order(), form!.Entity.Values["Categories"]!.Split(',').Order());
        Assert.Equal("Angular, CLI", form.Entity.Display!["Categories"]);
        Assert.Equal("AI", form.Entity.Display["Tags"]);
        Assert.Contains(form.Schema.Fields, f => f.Name == "Categories" && f.RefSchema == EntityConstants.Content.CategorySchemaIri);

        // the same set in another order changes nothing; a different set replaces the old one; empty clears it
        await _posts.UpdateAsync(id, Data(("Categories", $"{cli},{angular}")));
        await _posts.UpdateAsync(id, Data(("Categories", cli)));
        Assert.Equal(cli, (await _posts.GetFormAsync(id))!.Entity.Values["Categories"]);
        await _posts.UpdateAsync(id, Data(("Tags", string.Empty)));
        Assert.Null((await _posts.GetFormAsync(id))!.Entity.Values["Tags"]);
    }

    [Fact]
    public async Task PointersMustExist_AndALookupOffersTheCategories()
    {
        await NewCategoryAsync("Angular");

        var bad = await Assert.ThrowsAsync<InputValidationException>(() => _posts.CreateAsync(Data(("Title", "Post"), ("Categories", "no-such-category"))));
        Assert.Contains("does not exist", bad.Errors["Categories"].Single());

        var options = await _services.GetRequiredService<IEntityRelationProvider>().GetOptionsAsync(EntityConstants.Content.CategorySchemaIri);
        Assert.Equal(["Angular"], options.Select(o => o.Text));
    }
}
