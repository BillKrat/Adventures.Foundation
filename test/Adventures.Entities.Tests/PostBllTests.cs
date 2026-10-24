// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
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
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-013-post-approval.nq"));
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
    public async Task Approval_IsSetOnlyByTheServer_RecordsWhoAndWhen_AndIsClearedByAnyOtherState()
    {
        var id = (await _posts.CreateAsync(Data(("Title", "Review summary")))).Entity.EntityId;

        var attempt = await Assert.ThrowsAsync<InputValidationException>(() => _posts.UpdateAsync(id, Data(("Approval", "Approved"))));
        Assert.Contains("cannot be changed", attempt.Errors["Approval"].Single());

        Assert.True(await _bll.SetApprovalAsync(id, PostApproval.Approved, "Admin"));
        var approved = (await _posts.GetFormAsync(id))!.Entity.Values;
        Assert.Equal("Approved", approved["Approval"]);
        Assert.Equal("Admin", approved["ApprovedBy"]);
        Assert.False(string.IsNullOrEmpty(approved["ApprovedAt"]));

        Assert.True(await _bll.SetApprovalAsync(id, PostApproval.ChangesRequested, "Admin"));
        var requested = (await _posts.GetFormAsync(id))!.Entity.Values;
        Assert.Equal("Changes requested", requested["Approval"]);
        Assert.True(string.IsNullOrEmpty(requested.GetValueOrDefault("ApprovedBy")));
        Assert.True(string.IsNullOrEmpty(requested.GetValueOrDefault("ApprovedAt")));
    }

    [Fact]
    public async Task Approval_RefusesAnUnknownState_AProposedRevision_AndAMissingPost()
    {
        var id = (await _posts.CreateAsync(Data(("Title", "A post")))).Entity.EntityId;
        await _bll.PublishAsync(id);
        var revision = (await _posts.CreateAsync(Data(("Title", "A post, revised")))).Entity.EntityId;
        await _bll.MarkRevisionAsync(revision, id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _bll.SetApprovalAsync(id, "Maybe", "Admin"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _bll.SetApprovalAsync(revision, PostApproval.Approved, "Admin"));
        Assert.False(await _bll.SetApprovalAsync(Guid.NewGuid().ToString(), PostApproval.Approved, "Admin"));
    }

    [Fact]
    public async Task TwoPostsNeverShareAnAddress_AGeneratedOneGetsASuffix_ATypedOneIsRefused_AndARevisionNeverBlocks()
    {
        var first = (await _posts.CreateAsync(Data(("Title", "Release notes")))).Entity;
        var second = (await _posts.CreateAsync(Data(("Title", "Release notes")))).Entity;
        Assert.Equal("release-notes", first.Values["Slug"]);
        Assert.Equal("release-notes-2", second.Values["Slug"]);

        var typed = await Assert.ThrowsAsync<BusinessRuleException>(() => _posts.CreateAsync(Data(("Title", "Other"), ("Slug", "Release-Notes"))));
        Assert.Contains("Release notes", typed.Message);

        var moved = await Assert.ThrowsAsync<BusinessRuleException>(() => _posts.UpdateAsync(second.EntityId, Data(("Slug", "release-notes"))));
        Assert.Contains("already used", moved.Message);
        var saved = await _posts.UpdateAsync(second.EntityId, Data(("Title", "Release notes 2")));   // the slug is untouched: fine
        Assert.Equal("release-notes-2", saved!.Entity.Values["Slug"]);

        await _bll.PublishAsync(first.EntityId);
        var revision = (await _posts.CreateAsync(Data(("Title", "Release notes")))).Entity.EntityId;
        await _bll.MarkRevisionAsync(revision, first.EntityId);
        var next = (await _posts.CreateAsync(Data(("Title", "Brand new"), ("Slug", "release-notes-3")))).Entity;   // the hidden revision holds no address
        Assert.Equal("release-notes-3", next.Values["Slug"]);
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
