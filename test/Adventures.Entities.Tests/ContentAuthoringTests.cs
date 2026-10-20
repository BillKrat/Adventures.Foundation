using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>The drafts-only authoring surface: drafts under a fixed author, taxonomy by name, no way to publish or to touch a published or foreign post.</summary>
public sealed class ContentAuthoringTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private IContentAuthoring _authoring = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-006-content.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-007-post-taxonomy.nq"));
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
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        services.AddScoped<IEntityBll<Category>>(p => new EntityBll<Category>(p.GetRequiredService<IEntityRepository<Category>>()));
        services.AddScoped<IEntityBll<Tag>>(p => new EntityBll<Tag>(p.GetRequiredService<IEntityRepository<Tag>>()));
        services.AddScoped<IEntityPresenter<Post>, PostPresenter>();
        services.AddScoped<IEntityPresenter<Category>>(p => new EntityPresenter<Category>(
            p.GetRequiredService<IEntityBll<Category>>(), p.GetRequiredService<EntitySchemaOf<Category>>().Schema, s => new Category(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IEntityPresenter<Tag>>(p => new EntityPresenter<Tag>(
            p.GetRequiredService<IEntityBll<Tag>>(), p.GetRequiredService<EntitySchemaOf<Tag>>().Schema, s => new Tag(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IContentAuthoring, ContentAuthoring>();
        _services = services.BuildServiceProvider();
        _authoring = _services.GetRequiredService<IContentAuthoring>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ADraftIsCreatedUnderTheAuthor_FiledByName_AndNeverPublished()
    {
        var created = await _authoring.CreateDraftAsync(
            "Claude",
            new DraftPost("How Pages Work", Content: "# Pages\n\nA page has a parent.", Description: "Admin help", Categories: ["Help", "Admin"], Tags: ["Pages", "help-admin"]));

        Assert.False(created.IsPublished);
        Assert.Equal("how-pages-work", created.Slug);
        Assert.Equal(["Admin", "Help"], created.Categories.Order());
        Assert.Equal(["help-admin", "Pages"], created.Tags.Order(StringComparer.OrdinalIgnoreCase));
        var mine = await _authoring.ListMineAsync("Claude");
        Assert.Equal([created.Id], mine.Select(p => p.Id));
        Assert.Empty(await _authoring.ListMineAsync("Admin"));
    }

    [Fact]
    public async Task CategoriesAndTags_AreFoundByNameInAnyCase_NotDuplicated()
    {
        var first = await _authoring.EnsureCategoryAsync("Development log");
        var again = await _authoring.EnsureCategoryAsync("  development LOG ");
        await _authoring.CreateDraftAsync("Claude", new DraftPost("A", Categories: ["DEVELOPMENT LOG"]));

        Assert.Equal(first.Id, again.Id);
        Assert.Single(await _authoring.ListCategoriesAsync());
        Assert.Empty(await _authoring.ListTagsAsync());
        Assert.Equal("MCP", (await _authoring.EnsureTagAsync("MCP")).Name);
    }

    [Fact]
    public async Task ADraftCanBeRevised_WhileItIsAStillADraft_AndOnlyTheNamedFieldsChange()
    {
        var created = await _authoring.CreateDraftAsync("Claude", new DraftPost("Original", Content: "v1", Categories: ["Help"]));

        var revised = await _authoring.UpdateDraftAsync("Claude", created.Id, new DraftPost("Original", Content: "v2"));

        Assert.NotNull(revised);
        Assert.Equal(["Help"], revised!.Categories);
        var form = await _services.GetRequiredService<IEntityPresenter<Post>>().GetFormAsync(created.Id);
        Assert.Equal("v2", form!.Entity.Values["Content"]);
        Assert.Null(await _authoring.UpdateDraftAsync("Claude", "no-such-post", new DraftPost("x")));
    }

    [Fact]
    public async Task APublishedPost_AndSomeoneElsesPost_CannotBeChangedThroughAuthoring()
    {
        var created = await _authoring.CreateDraftAsync("Claude", new DraftPost("Mine"));

        // someone else (the Admin user) cannot be edited as Claude: the draft belongs to Claude
        var foreign = await _authoring.CreateDraftAsync("Admin", new DraftPost("Not Claude's"));
        var notMine = await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.UpdateDraftAsync("Claude", foreign.Id, new DraftPost("Hijack")));
        Assert.Contains("someone else", notMine.Message);

        // an administrator publishes it (a separate action the authoring surface does not have); from then on it is read-only here
        await _services.GetRequiredService<IPostBll>().PublishAsync(created.Id);
        var published = await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.UpdateDraftAsync("Claude", created.Id, new DraftPost("Edited after publish")));
        Assert.Contains("published", published.Message);
    }

    [Fact]
    public async Task AnUnknownAuthorIsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.CreateDraftAsync("NoSuchUser", new DraftPost("x")));
    }
}
