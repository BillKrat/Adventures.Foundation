// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Adventures.Blog;
using Adventures.Collaboration;

using Adventures.Entities;

namespace Adventures.Blog.Tests;

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
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-008-post-revision.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-013-post-approval.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-014-comments.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        const string graph = BlogConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, CollaborationConstants.CommentSchemaIri), CollaborationConstants.CommentBaseIri, CollaborationConstants.CommentTypeIri, graph, s => new Comment(s));
        services.AddScoped<IEntityBll<Comment>>(p => new EntityBll<Comment>(new EntityDal<Comment>(p.GetRequiredService<IEntityRepository<Comment>>())));
        services.AddScoped<IConversation, Conversation>();
        services.AddScoped<IConversationTarget, PostConversationTarget>();
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.PostSchemaIri), BlogConstants.Content.PostBaseIri, BlogConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.CategorySchemaIri), BlogConstants.Content.CategoryBaseIri, BlogConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.TagSchemaIri), BlogConstants.Content.TagBaseIri, BlogConstants.Content.TagTypeIri, graph, s => new Tag(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Schema.UserIri), EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, EntityConstants.User.DefaultGraph, s => new User(s));
        services.AddNQuadRelations();
        services.AddScoped<IPostBll, PostBll>();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        services.AddScoped<IEntityBll<Category>>(p => new EntityBll<Category>(new EntityDal<Category>(p.GetRequiredService<IEntityRepository<Category>>())));
        services.AddScoped<IEntityBll<Tag>>(p => new EntityBll<Tag>(new EntityDal<Tag>(p.GetRequiredService<IEntityRepository<Tag>>())));
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

    private async Task<string> PublishedAsync(string title, string content = "v1")
    {
        var draft = await _authoring.CreateDraftAsync("Claude", new DraftPost(title, Content: content, Categories: ["Help"], Tags: ["Pages"], Description: "orig"));
        await _services.GetRequiredService<IPostBll>().PublishAsync(draft.Id);
        return draft.Id;
    }

    [Fact]
    public async Task TheConversationUnderAPost_IsAddedOnlyByOrToTheAuthor_OldestFirst_AndNeverCarriesADecisionFromTheAuthor()
    {
        var mine = await _authoring.CreateDraftAsync("Claude", new DraftPost("Review summary", Categories: ["Collaboration"]));
        var conversation = _services.GetRequiredService<IConversation>();

        await _authoring.AddCommentAsync("Claude", mine.Id, "  Answering item 1: yes.  ");
        await Task.Delay(10);
        await conversation.AddAsync(mine.Id, "Admin", CommentKinds.Decision, "Decided: site settings go ahead.");

        var thread = await _authoring.ListCommentsAsync("Claude", mine.Id);
        Assert.Equal(["Answering item 1: yes.", "Decided: site settings go ahead."], thread.Select(e => e.Body));
        Assert.Equal(["Comment", "Decision"], thread.Select(e => e.Kind));
        Assert.Equal(["Claude", "Admin"], thread.Select(e => e.Author));
        Assert.All(thread, e => Assert.False(string.IsNullOrEmpty(e.Created)));
        Assert.Empty(await conversation.ListAsync(Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task TheDecisionsList_HasOnlyDecisions_NewestFirst_WithTheirPost()
    {
        var first = await _authoring.CreateDraftAsync("Claude", new DraftPost("First summary"));
        var second = await _authoring.CreateDraftAsync("Claude", new DraftPost("Second summary"));
        var conversation = _services.GetRequiredService<IConversation>();
        await conversation.AddAsync(first.Id, "Admin", CommentKinds.Decision, "Site settings go ahead.");
        await Task.Delay(10);
        await conversation.AddAsync(first.Id, "Claude", CommentKinds.Comment, "Noted.");
        await conversation.AddAsync(second.Id, "Admin", CommentKinds.Decision, "Collaborators role waits for a second person.");

        var decisions = await conversation.ListDecisionsAsync();

        Assert.Equal(["Collaborators role waits for a second person.", "Site settings go ahead."], decisions.Select(d => d.Body));
        Assert.Equal(["Second summary", "First summary"], decisions.Select(d => d.PostTitle));
        Assert.Equal([second.Id, first.Id], decisions.Select(d => d.PostId));
        Assert.All(decisions, d => Assert.Equal("Admin", d.Author));
    }

    [Fact]
    public async Task AConversationEntry_IsRefusedWhenEmptyTooLongOfAnUnknownKindOrForAnotherAuthorsPost()
    {
        var mine = await _authoring.CreateDraftAsync("Claude", new DraftPost("Mine"));
        var conversation = _services.GetRequiredService<IConversation>();
        var users = _services.GetRequiredService<IUserPresenter>();
        var other = users.GetFormByUserNameAsync("Admin").Result!.Entity.EntityId;
        _ = other;

        await Assert.ThrowsAsync<BusinessRuleException>(() => conversation.AddAsync(mine.Id, "Admin", CommentKinds.Comment, "   "));
        await Assert.ThrowsAsync<BusinessRuleException>(() => conversation.AddAsync(mine.Id, "Admin", CommentKinds.Comment, new string('x', Conversation.MaxBodyLength + 1)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => conversation.AddAsync(mine.Id, "Admin", "Shout", "hello"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => conversation.AddAsync(Guid.NewGuid().ToString(), "Admin", CommentKinds.Comment, "hello"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.AddCommentAsync("Admin", mine.Id, "not my post"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.ListCommentsAsync("Admin", mine.Id));
    }

    [Fact]
    public async Task ACollaborationDraft_StartsAsNeedsReview_AnOrdinaryDraftHasNoReviewState()
    {
        var collab = await _authoring.CreateDraftAsync("Claude", new DraftPost("Review summary", Categories: ["Collaboration"]));
        var ordinary = await _authoring.CreateDraftAsync("Claude", new DraftPost("A normal draft", Categories: ["Help"]));

        Assert.Equal(PostApproval.NeedsReview, collab.Approval);
        Assert.Null(ordinary.Approval);
        Assert.Equal(PostApproval.NeedsReview, (await _authoring.ListMineAsync("Claude")).Single(p => p.Title == "Review summary").Approval);
    }

    [Fact]
    public async Task ChangingAnApprovedOrRejectedCollaborationDraft_PutsItBackToNeedsReview()
    {
        var bll = _services.GetRequiredService<IPostBll>();
        var draft = await _authoring.CreateDraftAsync("Claude", new DraftPost("Review summary", Categories: ["Collaboration"]));
        await bll.SetApprovalAsync(draft.Id, PostApproval.Approved, "Admin");

        var edited = await _authoring.UpdateDraftAsync("Claude", draft.Id, new DraftPost(string.Empty, Content: "second version"));

        Assert.Equal(PostApproval.NeedsReview, edited!.Approval);

        await bll.SetApprovalAsync(draft.Id, PostApproval.ChangesRequested, "Admin");
        var again = await _authoring.UpdateDraftAsync("Claude", draft.Id, new DraftPost(string.Empty, Content: "third version"));

        Assert.Equal(PostApproval.NeedsReview, again!.Approval);
    }

    [Fact]
    public async Task ARevisionOfAPublishedPost_IsAHiddenDraftCopyWithTheChanges_AndTheOriginalIsUntouched()
    {
        var id = await PublishedAsync("Pages help");

        var revision = await _authoring.ProposeRevisionAsync("Claude", id, new DraftPost(string.Empty, Content: "v2 with new steps"));

        Assert.NotNull(revision);
        Assert.False(revision!.IsPublished);
        Assert.Equal(id, revision.RevisionOf);
        Assert.Equal("Pages help", revision.Title);                // untouched fields carry over from the published post
        Assert.Equal(["Help"], revision.Categories);
        var presenter = _services.GetRequiredService<IEntityPresenter<Post>>();
        Assert.Equal("v1", (await presenter.GetFormAsync(id))!.Entity.Values["Content"]);  // readers still see v1
        Assert.Equal("v2 with new steps", (await presenter.GetFormAsync(revision.Id))!.Entity.Values["Content"]);
    }

    [Fact]
    public async Task TheListNamesTheOriginalOfARevision_AndLeavesOrdinaryPostsBlank()
    {
        var id = await PublishedAsync("Pages help");
        var revision = await _authoring.ProposeRevisionAsync("Claude", id, new DraftPost(string.Empty, Content: "v2"));
        var presenter = _services.GetRequiredService<IEntityPresenter<Post>>();

        var grid = await presenter.GetGridAsync(new EntityGridQuery());

        var revisionRow = grid.Rows.Single(r => r.EntityId == revision!.Id);
        var originalRow = grid.Rows.Single(r => r.EntityId == id);
        Assert.Equal(id, revisionRow.Values["RevisionOf"]);
        Assert.Equal("Pages help", revisionRow.Display!["RevisionOf"]);
        Assert.False(originalRow.Display?.ContainsKey("RevisionOf") ?? false);
    }

    [Fact]
    public async Task AskingAgain_UpdatesTheOneOpenRevision_NotAnotherCopy()
    {
        var id = await PublishedAsync("Pages help");
        var first = await _authoring.ProposeRevisionAsync("Claude", id, new DraftPost(string.Empty, Content: "v2"));

        var second = await _authoring.ProposeRevisionAsync("Claude", id, new DraftPost(string.Empty, Content: "v3"));

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(2, (await _authoring.ListMineAsync("Claude")).Count);
    }

    [Fact]
    public async Task ApplyingARevision_UpdatesTheOriginalKeepingItsAddress_AndRemovesTheRevision()
    {
        var id = await PublishedAsync("Pages help");
        var revision = await _authoring.ProposeRevisionAsync("Claude", id, new DraftPost("Pages help, revised", Content: "v2", Categories: ["Help", "Admin"]));
        var bll = _services.GetRequiredService<IPostBll>();
        var presenter = _services.GetRequiredService<IEntityPresenter<Post>>();

        Assert.True(await bll.ApplyRevisionAsync(revision!.Id));

        var form = await presenter.GetFormAsync(id);
        Assert.Equal("Pages help, revised", form!.Entity.Values["Title"]);
        Assert.Equal("v2", form.Entity.Values["Content"]);
        Assert.Equal("pages-help", form.Entity.Values["Slug"]);   // the published address never changes
        Assert.Equal("true", form.Entity.Values["IsPublished"]);
        Assert.Equal(["Admin", "Help"], form.Entity.Display!["Categories"].Split(", ").Order());
        Assert.Null(await presenter.GetFormAsync(revision.Id));
        Assert.False(await bll.ApplyRevisionAsync(revision.Id));
    }

    [Fact]
    public async Task ARevisionCannotBePublished_CanBeDiscarded_AndTheOtherCasesAreRefused()
    {
        var id = await PublishedAsync("Pages help");
        var revision = await _authoring.ProposeRevisionAsync("Claude", id, new DraftPost(string.Empty, Content: "v2"));
        var bll = _services.GetRequiredService<IPostBll>();

        var publish = await Assert.ThrowsAsync<BusinessRuleException>(() => bll.PublishAsync(revision!.Id));
        Assert.Contains("apply or discard", publish.Message);
        Assert.True(await bll.DiscardRevisionAsync(revision!.Id));
        Assert.Single(await _authoring.ListMineAsync("Claude"));

        var draft = await _authoring.CreateDraftAsync("Claude", new DraftPost("Still a draft"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.ProposeRevisionAsync("Claude", draft.Id, new DraftPost(string.Empty, Content: "x")));
        var foreign = await _authoring.CreateDraftAsync("Admin", new DraftPost("Admins post"));
        await _services.GetRequiredService<IPostBll>().PublishAsync(foreign.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.ProposeRevisionAsync("Claude", foreign.Id, new DraftPost(string.Empty, Content: "x")));
        Assert.Null(await _authoring.ProposeRevisionAsync("Claude", "no-such-post", new DraftPost(string.Empty)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => bll.DiscardRevisionAsync(id)); // a normal post is not a revision
    }
}
