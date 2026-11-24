// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Adventures.Blog;
using Adventures.Collaboration;

using Adventures.Entities;

namespace Adventures.Blog.Tests;

/// <summary>Collaboration items: their own type (never a post), reviewed only when Developer, authored by an AI without a way to approve, with the shared conversation.</summary>
public sealed class CollaborationTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;
    private ICollaborationBll _bll = null!;
    private ICollaborationAuthoring _authoring = null!;
    private IConversation _conversation = null!;
    private IPostBll _posts = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-006-content.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-007-post-taxonomy.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-008-post-revision.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-013-post-approval.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-014-comments.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-015-collaboration.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-017-research.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);

        const string graph = BlogConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.PostSchemaIri), BlogConstants.Content.PostBaseIri, BlogConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.CategorySchemaIri), BlogConstants.Content.CategoryBaseIri, BlogConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, CollaborationConstants.CommentSchemaIri), CollaborationConstants.CommentBaseIri, CollaborationConstants.CommentTypeIri, graph, s => new Comment(s));
        services.AddScoped<IEntityBll<Comment>>(p => new EntityBll<Comment>(new EntityDal<Comment>(p.GetRequiredService<IEntityRepository<Comment>>())));
        services.AddScoped<IConversation, Conversation>();
        services.AddScoped<IConversationTarget, PostConversationTarget>();
        services.AddScoped<IConversationTarget, CollaborationConversationTarget>();
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, UserConstants.SchemaIri), UserConstants.BaseIri, UserConstants.TypeIri, UserConstants.DefaultGraph, s => new User(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, CollaborationConstants.SchemaIri), CollaborationConstants.BaseIri, CollaborationConstants.TypeIri, CollaborationConstants.DefaultGraph, s => new CollaborationItem(s));
        services.AddNQuadRelations();
        services.AddScoped<IPostBll, PostBll>();
        services.AddScoped<ICollaborationBll, CollaborationBll>();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        services.AddScoped<ICollaborationAuthoring, CollaborationAuthoring>();
        services.AddScoped<CollaborationMigration>();
        services.AddScoped<IEntityBll<Category>>(p => new EntityBll<Category>(new EntityDal<Category>(p.GetRequiredService<IEntityRepository<Category>>())));
        _services = services.BuildServiceProvider();
        _bll = _services.GetRequiredService<ICollaborationBll>();
        _authoring = _services.GetRequiredService<ICollaborationAuthoring>();
        _conversation = _services.GetRequiredService<IConversation>();
        _posts = _services.GetRequiredService<IPostBll>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ADeveloperItemStartsAsNeedingReview_UnderTheAuthor_AndIsNotAPost()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Review the plan", Content: "# Plan", Branch: "claude-x", Repos: "app"));

        Assert.Equal(CollaborationKinds.Developer, created.Kind);
        Assert.Equal(PostApproval.NeedsReview, created.Approval);
        Assert.Equal([created.Id], (await _authoring.ListMineAsync("Claude")).Select(i => i.Id));
        Assert.Empty(await _authoring.ListMineAsync("Admin"));
        Assert.Empty(await _posts.ListAsync());
    }

    [Fact]
    public async Task AResearchItemHasNoApproval_AndCannotBeApproved()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Is X possible?", Kind: CollaborationKinds.Research));

        Assert.Null(created.Approval);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _bll.SetApprovalAsync(created.Id, PostApproval.Approved, "Admin"));
    }

    [Fact]
    public async Task ApprovingRecordsWhoAndWhen_AndRevisingAsTheAuthorSendsItBackToReview()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Review me"));
        Assert.True(await _bll.SetApprovalAsync(created.Id, PostApproval.Approved, "Admin"));
        var approved = await _bll.GetAsync(created.Id);
        Assert.Equal("Admin", approved!.GetValue("ApprovedBy"));
        Assert.NotNull(approved.GetValue("ApprovedAt"));

        var revised = await _authoring.UpdateAsync("Claude", created.Id, new CollaborationDraft(Content: "changed"));

        Assert.Equal(PostApproval.NeedsReview, revised!.Approval);
        Assert.Null((await _bll.GetAsync(created.Id))!.GetValue("ApprovedBy"));
    }

    [Fact]
    public async Task TheAuthoringAccountCanReadAnItemTheHumanWrote()
    {
        // the user usage review is the example: the AI must be able to read the human's items to act on them
        var humans = await _authoring.CreateAsync("Admin", new CollaborationDraft("User usage review", Content: "1. Move Add to the grid", Kind: CollaborationKinds.Research, Findings: "two clicks"));
        await _conversation.AddAsync(humans.Id, "Admin", CommentKinds.Comment, "Please start with the grid buttons");

        var detail = await _authoring.GetAsync("Claude", humans.Id);
        var comments = await _authoring.ListCommentsAsync("Claude", humans.Id);

        Assert.Equal("1. Move Add to the grid", detail!.Content);
        Assert.Equal("two clicks", detail.Findings);
        Assert.False(detail.Summary.Mine);
        Assert.Contains(comments, c => c.Body.Contains("grid buttons", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheAuthoringAccountCanEditAndCommentOnTheHumansItem_ThroughANoteTheHumanSees_AndNeverKeepsAnApproval()
    {
        var humans = await _authoring.CreateAsync("Admin", new CollaborationDraft("Review the plan", Content: "v1"));
        Assert.True(await _bll.SetApprovalAsync(humans.Id, PostApproval.Approved, "Admin"));

        var edited = await _authoring.UpdateAsync("Claude", humans.Id, new CollaborationDraft(Content: "v2, answered the findings"));
        await _authoring.AddCommentAsync("Claude", humans.Id, "Done: see the content");

        Assert.Equal("v2, answered the findings", (await _authoring.GetAsync("Claude", humans.Id))!.Content);
        Assert.Equal(PostApproval.NeedsReview, edited!.Approval); // an approval of v1 does not describe v2
        Assert.False(edited.Mine);
        var comments = await _authoring.ListCommentsAsync("Admin", humans.Id);
        Assert.Contains(comments, c => c.Author == "Claude" && c.Body.Contains("Edited by Claude", StringComparison.Ordinal) && c.Body.Contains("content", StringComparison.Ordinal));
        Assert.Contains(comments, c => c.Body == "Done: see the content");
        // the item is still the human's
        Assert.Equal("Admin", (await _authoring.ListAllAsync("Admin")).Single(i => i.Id == humans.Id).Mine == true ? "Admin" : "?");
    }

    [Fact]
    public async Task EditingTheAiItemByAnotherAccountToo_StillNeedsTheItemToExist()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.AddCommentAsync("Claude", Guid.NewGuid().ToString(), "x"));
        Assert.Null(await _authoring.UpdateAsync("Claude", Guid.NewGuid().ToString(), new CollaborationDraft("x")));
    }
    [Fact]
    public async Task ListAllShowsEveryItemWithAMineFlag()
    {
        var mine = await _authoring.CreateAsync("Claude", new CollaborationDraft("Mine"));
        var theirs = await _authoring.CreateAsync("Admin", new CollaborationDraft("Theirs"));

        var all = await _authoring.ListAllAsync("Claude");

        Assert.True(all.Single(i => i.Id == mine.Id).Mine);
        Assert.False(all.Single(i => i.Id == theirs.Id).Mine);
        Assert.Equal([mine.Id], (await _authoring.ListMineAsync("Claude")).Select(i => i.Id)); // the old list stays "mine"
    }

    [Fact]
    public async Task TheAuthorCanReadTheBodyOfItsOwnItem()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Read me", Content: "## Test steps\n1. Run it", Kind: CollaborationKinds.Research, Sources: "src", Findings: "found"));

        var detail = await _authoring.GetAsync("Claude", created.Id);

        Assert.Equal("## Test steps\n1. Run it", detail!.Content);
        Assert.Equal("src", detail.Sources);
        Assert.Equal("found", detail.Findings);
        Assert.Equal("Read me", detail.Summary.Title);
        Assert.False(detail.Truncated);
        Assert.True(detail.Summary.Mine);
        Assert.Null(await _authoring.GetAsync("Claude", Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task TheBodyIsCappedAndSaysSo()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Long", Content: new string('x', CollaborationAuthoring.MaxBodyCharacters + 50)));

        var detail = await _authoring.GetAsync("Claude", created.Id);

        Assert.Equal(CollaborationAuthoring.MaxBodyCharacters, detail!.Content!.Length);
        Assert.True(detail.Truncated);
    }

    [Fact]
    public async Task TheSharedConversationWorksUnderAnItem_AndItsDecisionsAreListedWithTheirSource()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Decide this"));

        await _authoring.AddCommentAsync("Claude", created.Id, "Question for you");
        await _conversation.AddAsync(created.Id, "Admin", CommentKinds.Decision, "Yes, go ahead");

        Assert.Equal(2, (await _conversation.ListAsync(created.Id)).Count);
        var decision = Assert.Single(await _conversation.ListDecisionsAsync());
        Assert.Equal("collaboration", decision.Source);
        Assert.Equal("Decide this", decision.PostTitle);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _conversation.AddAsync("no-such-id", "Admin", CommentKinds.Comment, "x"));
    }

    [Fact]
    public async Task TheMigrationMovesCollaborationDraftsKeepingTheirIds_AfterADryRunThatChangesNothing()
    {
        var categories = _services.GetRequiredService<IEntityBll<Category>>();
        var collab = new Category(_services.GetRequiredService<EntitySchemaOf<Category>>().Schema);
        collab.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        collab.Set("Name", Guid.NewGuid().ToString(), "Collaboration");
        var category = await categories.CreateAsync(collab);
        var post = new Post(_services.GetRequiredService<EntitySchemaOf<Post>>().Schema);
        post.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        post.Set("Title", Guid.NewGuid().ToString(), "An old review");
        post.Set("Content", Guid.NewGuid().ToString(), "# body");
        post.Add("Categories", Guid.NewGuid().ToString(), category.EntityId!.Value);
        post = await _posts.CreateAsync(post);
        await _posts.SetApprovalAsync(post.EntityId!.Value, PostApproval.Approved, "Admin");
        var id = post.EntityId!.Value;
        await _conversation.AddAsync(id, "Admin", CommentKinds.Decision, "Go");
        var migration = _services.GetRequiredService<CollaborationMigration>();

        var dry = await migration.RunAsync(apply: false, CancellationToken.None);

        Assert.Equal(id, Assert.Single(dry.Posts).Id);
        Assert.Empty(await _bll.ListAsync());
        Assert.NotNull(await _posts.GetAsync(id));

        var done = await migration.RunAsync(apply: true, CancellationToken.None);

        Assert.Single(done.Posts);
        var item = await _bll.GetAsync(id);
        Assert.Equal("An old review", item!.GetValue("Title"));
        Assert.Equal(PostApproval.Approved, item.GetValue("Approval"));
        Assert.Equal(CollaborationKinds.Developer, item.GetValue("Kind"));
        Assert.Null(await _posts.GetAsync(id));
        Assert.Single(await _conversation.ListDecisionsAsync());
        Assert.Empty((await migration.RunAsync(apply: true, CancellationToken.None)).Posts);
    }

    [Fact]
    public async Task AResearchItemStartsOpen_AndMovesThroughItsOwnStates_WithSourcesAndFindings()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Is X possible?", Kind: CollaborationKinds.Research, Sources: "- paper A", Findings: "Probably."));

        Assert.Equal(ResearchStatus.Open, created.Status);
        Assert.Null(created.Approval);
        Assert.True(await _bll.SetStatusAsync(created.Id, ResearchStatus.Accepted));
        var stored = await _bll.GetAsync(created.Id);
        Assert.Equal(ResearchStatus.Accepted, stored!.GetValue("Status"));
        Assert.Equal("- paper A", stored.GetValue("Sources"));
        Assert.Equal("Probably.", stored.GetValue("Findings"));
        Assert.False(await _bll.SetStatusAsync("no-such-id", ResearchStatus.Closed));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _bll.SetStatusAsync(created.Id, "Weird"));
    }

    [Fact]
    public async Task ADeveloperItemHasNoResearchStatus_AndRevisingAResearchItemKeepsItsStatus()
    {
        var developer = await _authoring.CreateAsync("Claude", new CollaborationDraft("Review"));
        Assert.Null(developer.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _bll.SetStatusAsync(developer.Id, ResearchStatus.Closed));

        var research = await _authoring.CreateAsync("Claude", new CollaborationDraft("Question", Kind: CollaborationKinds.Research));
        await _bll.SetStatusAsync(research.Id, ResearchStatus.Closed);
        var revised = await _authoring.UpdateAsync("Claude", research.Id, new CollaborationDraft(Findings: "More"));

        Assert.Equal(ResearchStatus.Closed, revised!.Status);
    }

    [Fact]
    public async Task TheFormShowsTitleThenContent_LikePostsAndPages_AndTheRestKeepTheirSequence()
    {
        var schema = _services.GetRequiredService<EntitySchemaOf<CollaborationItem>>();
        var presenter = new CollaborationPresenter(_bll, schema, _services.GetRequiredService<IEntityRelationProvider>());
        var rest = schema.Schema.Fields.Values.Where(f => f.Name is not ("Title" or "Content")).OrderBy(f => f.Order ?? int.MaxValue).Select(f => f.Name).ToList();

        var blank = (await presenter.GetNewFormAsync())!.Schema.Fields.Select(f => f.Name).ToList();

        Assert.Equal(["Title", "Content"], blank.Take(2));
        Assert.Equal(rest, blank.Skip(2));
    }

    [Fact]
    public async Task AGridFilterKeepsOnlyTheMatchingRows_IgnoresAnUnknownField_AndIsRemembered()
    {
        await _authoring.CreateAsync("Claude", new CollaborationDraft("Review"));
        await _authoring.CreateAsync("Claude", new CollaborationDraft("Question", Kind: CollaborationKinds.Research));
        var all = await _bll.ListAsync();
        var schema = _services.GetRequiredService<EntitySchemaOf<CollaborationItem>>().Schema;

        var (rows, paging) = EntityGridPager.Apply(schema, all, new EntityGridQuery(Filter: "kind", FilterValue: "research"));

        Assert.Equal("Question", Assert.Single(rows).GetValue("Title"));
        Assert.Equal(1, paging.TotalCount);
        Assert.Equal("Kind", paging.Filter);
        Assert.Equal(2, EntityGridPager.Apply(schema, all, new EntityGridQuery(Filter: "NoSuchField", FilterValue: "x")).Page.Count);
        Assert.Equal(2, EntityGridPager.Apply(schema, all, new EntityGridQuery()).Page.Count);
    }
}
