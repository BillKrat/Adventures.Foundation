using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>What a visitor may read: published posts and pages only, by slug, filtered, paged, with the menu tree and the front page.</summary>
public sealed class PublicContentTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IPublicContent _public = null!;
    private IContentAuthoring _authoring = null!;
    private IPostBll _postBll = null!;
    private IEntityPresenter<Page> _pages = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var file in new[] { "seed.nq", "seed-006-content.nq", "seed-007-post-taxonomy.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, file));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        const string graph = EntityConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.PostSchemaIri), EntityConstants.Content.PostBaseIri, EntityConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.PageSchemaIri), EntityConstants.Content.PageBaseIri, EntityConstants.Content.PageTypeIri, graph, s => new Page(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.CategorySchemaIri), EntityConstants.Content.CategoryBaseIri, EntityConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.TagSchemaIri), EntityConstants.Content.TagBaseIri, EntityConstants.Content.TagTypeIri, graph, s => new Tag(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Schema.UserIri), EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, EntityConstants.User.DefaultGraph, s => new User(s));
        services.AddNQuadRelations();
        services.AddScoped<IPostBll, PostBll>();
        services.AddScoped<IPageBll, PageBll>();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        services.AddScoped<IEntityBll<Category>>(p => new EntityBll<Category>(p.GetRequiredService<IEntityRepository<Category>>()));
        services.AddScoped<IEntityBll<Tag>>(p => new EntityBll<Tag>(p.GetRequiredService<IEntityRepository<Tag>>()));
        services.AddScoped<IEntityPresenter<Post>, PostPresenter>();
        services.AddScoped<IEntityPresenter<Page>>(p => new EntityPresenter<Page>(
            p.GetRequiredService<IPageBll>(), p.GetRequiredService<EntitySchemaOf<Page>>().Schema, s => new Page(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IEntityPresenter<Category>>(p => new EntityPresenter<Category>(
            p.GetRequiredService<IEntityBll<Category>>(), p.GetRequiredService<EntitySchemaOf<Category>>().Schema, s => new Category(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IEntityPresenter<Tag>>(p => new EntityPresenter<Tag>(
            p.GetRequiredService<IEntityBll<Tag>>(), p.GetRequiredService<EntitySchemaOf<Tag>>().Schema, s => new Tag(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IContentAuthoring, ContentAuthoring>();
        services.AddScoped<IPublicContent, PublicContent>();
        _services = services.BuildServiceProvider();
        _public = _services.GetRequiredService<IPublicContent>();
        _authoring = _services.GetRequiredService<IContentAuthoring>();
        _postBll = _services.GetRequiredService<IPostBll>();
        _pages = _services.GetRequiredService<IEntityPresenter<Page>>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> PublishedPostAsync(string title, string[]? categories = null, string[]? tags = null, string content = "body")
    {
        var draft = await _authoring.CreateDraftAsync("Claude", new DraftPost(title, Content: content, Categories: categories, Tags: tags));
        await _postBll.PublishAsync(draft.Id);
        return draft.Id;
    }

    private async Task<string> PageAsync(string title, bool published = true, bool show = true, string? parent = null, int order = 0, bool front = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["Title"] = title,
            ["IsPublished"] = published ? "true" : "false",
            ["ShowInList"] = show ? "true" : "false",
            ["SortOrder"] = order.ToString(),
            ["IsFrontPage"] = front ? "true" : "false",
            ["Content"] = $"content of {title}",
        };
        if (parent is not null)
        {
            values["Parent"] = parent;
        }

        return (await _pages.CreateAsync(new EntityDataModel(string.Empty, values))).Entity.EntityId;
    }

    [Fact]
    public async Task OnlyPublishedPostsAreVisible_ByListAndBySlug_WithTheAuthorsDisplayNameOnly()
    {
        await _authoring.CreateDraftAsync("Claude", new DraftPost("Secret draft"));
        await PublishedPostAsync("Public post", categories: ["Help"], tags: ["MCP"], content: "# Hello");

        var list = await _public.ListPostsAsync(1, 10, null, null);
        var post = await _public.GetPostAsync("public-post");

        Assert.Equal(["Public post"], list.Posts.Select(p => p.Title));
        Assert.Equal(1, list.Total);
        Assert.Null(await _public.GetPostAsync("secret-draft"));
        Assert.Equal("# Hello", post!.Content);
        Assert.Equal("Claude", post.Summary.Author);
        Assert.Equal(["Help"], post.Summary.Categories);
        Assert.DoesNotContain("@", post.Summary.Author);
    }

    [Fact]
    public async Task PostsFilterByCategoryAndTag_AreCountedAndPaged()
    {
        for (var i = 1; i <= 5; i++)
        {
            await PublishedPostAsync($"Post {i}", categories: i <= 3 ? ["Help"] : ["Development log"], tags: i == 1 ? ["MCP"] : null);
        }

        Assert.Equal(3, (await _public.ListPostsAsync(1, 10, "help", null)).Total);
        Assert.Equal(["Post 1"], (await _public.ListPostsAsync(1, 10, null, "mcp")).Posts.Select(p => p.Title));
        var second = await _public.ListPostsAsync(2, 2, null, null);
        Assert.Equal(2, second.Posts.Count);
        Assert.Equal(5, second.Total);
        Assert.Equal(["Help", "Development log"], (await _public.GetCategoriesAsync()).Select(t => t.Name));
        Assert.Equal(3, (await _public.GetCategoriesAsync()).First().Posts);
        Assert.Equal([("MCP", 1)], (await _public.GetTagsAsync()).Select(t => (t.Name, t.Posts)));
    }

    [Fact]
    public async Task TheMenuIsThePublishedListedPageTree_InSortOrder_AndTheFrontPageIsFoundWhenSet()
    {
        var article = await PageAsync("Article", order: 20);
        await PageAsync("Angular", parent: article, order: 10);
        await PageAsync("Swagger", parent: article, order: 20);
        await PageAsync("Hidden from the menu", show: false);
        await PageAsync("A draft page", published: false);
        await PageAsync("About", order: 10);
        Assert.Null(await _public.GetFrontPageAsync());

        await PageAsync("Home", order: 0, front: true);

        var menu = await _public.GetMenuAsync();
        Assert.Equal(["Home", "About", "Article"], menu.Select(m => m.Title));
        Assert.Equal(["Angular", "Swagger"], menu.Single(m => m.Title == "Article").Children.Select(c => c.Title));
        Assert.Equal("Home", (await _public.GetFrontPageAsync())!.Title);
        Assert.Equal("Article", (await _public.GetPageAsync("angular"))!.ParentTitle);
        Assert.Equal("hidden-from-the-menu", (await _public.GetPageAsync("hidden-from-the-menu"))!.Slug); // reachable by URL, just not listed
        Assert.Null(await _public.GetPageAsync("a-draft-page"));
    }
}
