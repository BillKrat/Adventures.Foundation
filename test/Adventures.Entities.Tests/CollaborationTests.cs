using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

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
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);

        const string graph = EntityConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.PostSchemaIri), EntityConstants.Content.PostBaseIri, EntityConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Content.CommentSchemaIri), EntityConstants.Content.CommentBaseIri, EntityConstants.Content.CommentTypeIri, graph, s => new Comment(s));
        services.AddScoped<IEntityBll<Comment>>(p => new EntityBll<Comment>(p.GetRequiredService<IEntityRepository<Comment>>()));
        services.AddScoped<IConversation, Conversation>();
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Schema.UserIri), EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, EntityConstants.User.DefaultGraph, s => new User(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, CollaborationConstants.SchemaIri), CollaborationConstants.BaseIri, CollaborationConstants.TypeIri, CollaborationConstants.DefaultGraph, s => new CollaborationItem(s));
        services.AddNQuadRelations();
        services.AddScoped<IPostBll, PostBll>();
        services.AddScoped<ICollaborationBll, CollaborationBll>();
        services.AddScoped<IUserBll, UserBll>();
        services.AddScoped<IUserPresenter, UserPresenter>();
        services.AddScoped<ICollaborationAuthoring, CollaborationAuthoring>();
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
    public async Task AnItemOfAnotherAuthorCannotBeChangedOrRead()
    {
        var created = await _authoring.CreateAsync("Claude", new CollaborationDraft("Mine"));

        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.UpdateAsync("Admin", created.Id, new CollaborationDraft("Not yours")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _authoring.ListCommentsAsync("Admin", created.Id));
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
}
