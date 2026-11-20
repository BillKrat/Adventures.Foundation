// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Microsoft.AspNetCore.Http;
using Adventures.Common.Exceptions;
using Adventures.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;
using Adventures.Blog;
using Adventures.Collaboration;

using Adventures.Blog.WebApi;

namespace Adventures.Blog.WebApi.Tests;

public sealed class AuthoringControllerTests
{
    private sealed class FakeAuthoring : IContentAuthoring
    {
        public (string Author, DraftPost Draft)? LastCreate { get; private set; }

        public Exception? Throw { get; set; }

        public bool Missing { get; set; }

        private static DraftSummary Summary(string id = "p1") => new(id, "T", "t", false, null, [], []);

        public Task<DraftSummary> CreateDraftAsync(string authorUserName, DraftPost draft, CancellationToken cancellationToken = default)
        {
            LastCreate = (authorUserName, draft);
            return Throw is null ? Task.FromResult(Summary()) : throw Throw;
        }

        public Task<DraftSummary?> UpdateDraftAsync(string authorUserName, string postId, DraftPost draft, CancellationToken cancellationToken = default) =>
            Throw is null ? Task.FromResult<DraftSummary?>(Missing ? null : Summary(postId)) : throw Throw;

        public Task<DraftSummary?> ProposeRevisionAsync(string authorUserName, string publishedPostId, DraftPost changes, CancellationToken cancellationToken = default) =>
            Throw is null ? Task.FromResult<DraftSummary?>(Missing ? null : Summary(publishedPostId)) : throw Throw;

        public Task<IReadOnlyList<DraftSummary>> ListMineAsync(string authorUserName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DraftSummary>>([Summary()]);

        public (string Author, string PostId, string Text)? LastComment { get; private set; }

        public Task<IReadOnlyList<ConversationEntry>> ListCommentsAsync(string authorUserName, string postId, CancellationToken cancellationToken = default) =>
            Throw is null ? Task.FromResult<IReadOnlyList<ConversationEntry>>([new ConversationEntry("c1", "Admin", "Decision", "2026-10-23T10:00:00.0000000+00:00", "go")]) : throw Throw;

        public Task<ConversationEntry> AddCommentAsync(string authorUserName, string postId, string text, CancellationToken cancellationToken = default)
        {
            LastComment = (authorUserName, postId, text);
            return Throw is null ? Task.FromResult(new ConversationEntry("c2", authorUserName, "Comment", "2026-10-23T11:00:00.0000000+00:00", text)) : throw Throw;
        }

        public Task<IReadOnlyList<TaxonomyTerm>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TaxonomyTerm>>([]);

        public Task<IReadOnlyList<TaxonomyTerm>> ListTagsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TaxonomyTerm>>([]);

        public Task<TaxonomyTerm> EnsureCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(new TaxonomyTerm("c1", name));

        public Task<TaxonomyTerm> EnsureTagAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(new TaxonomyTerm("t1", name));
    }

    private static AuthoringController Build(FakeAuthoring authoring, string? configuredAuthor = null) =>
        new(authoring, new ConfigurationBuilder().AddInMemoryCollection(configuredAuthor is null ? [] : new Dictionary<string, string?> { ["Authoring:AuthorUserName"] = configuredAuthor }).Build());

    [Fact]
    public async Task Comments_AreReadAndAddedForTheConfiguredAuthor_AsPlainComments()
    {
        var authoring = new FakeAuthoring();
        var controller = Build(authoring, "Claude");

        var read = await controller.Comments("p1", CancellationToken.None);
        var added = await controller.AddComment("p1", new AuthoringCommentRequest("Answering item 2.", "Decision"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(read);
        Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)added).StatusCode);
        Assert.Equal(("Claude", "p1", "Answering item 2."), authoring.LastComment);   // the kind in the body is ignored: the authoring surface never records a decision
    }

    [Fact]
    public void ItIsProtectedByTheDraftScopeOnly_AndHasNoWayToPublish()
    {
        var authorize = Assert.Single(typeof(AuthoringController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Scope:content.draft", authorize.Policy);
        Assert.DoesNotContain(typeof(AuthoringController).GetMethods(), m => m.Name.Contains("Publish", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_Returns201_AndAlwaysUsesTheConfiguredAuthor_DefaultingToClaude()
    {
        var authoring = new FakeAuthoring();

        var result = await Build(authoring).Create(new DraftPost("Title"), CancellationToken.None);
        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("Claude", authoring.LastCreate!.Value.Author);

        await Build(authoring, "Writer").Create(new DraftPost("Title"), CancellationToken.None);
        Assert.Equal("Writer", authoring.LastCreate!.Value.Author);
    }

    [Fact]
    public async Task Update_Maps404_409_And400()
    {
        var authoring = new FakeAuthoring { Missing = true };
        var controller = Build(authoring);
        Assert.IsType<NotFoundResult>(await controller.Update("x", new DraftPost("T"), CancellationToken.None));

        authoring.Throw = new BusinessRuleException("This post is published.");
        Assert.IsType<ConflictObjectResult>(await controller.Update("x", new DraftPost("T"), CancellationToken.None));

        authoring.Throw = new InputValidationException(new Dictionary<string, string[]> { ["Title"] = ["Title is required."] });
        Assert.IsType<BadRequestObjectResult>(await controller.Update("x", new DraftPost("T"), CancellationToken.None));
    }

    [Fact]
    public async Task ProposeRevision_Maps200_404_And409()
    {
        var authoring = new FakeAuthoring();
        var controller = Build(authoring);
        Assert.IsType<OkObjectResult>(await controller.ProposeRevision("p1", new DraftPost(string.Empty), CancellationToken.None));

        authoring.Missing = true;
        Assert.IsType<NotFoundResult>(await controller.ProposeRevision("p1", new DraftPost(string.Empty), CancellationToken.None));

        authoring.Throw = new BusinessRuleException("This post is still a draft.");
        Assert.IsType<ConflictObjectResult>(await controller.ProposeRevision("p1", new DraftPost(string.Empty), CancellationToken.None));
    }

    [Fact]
    public async Task EnsuringATermNeedsAName()
    {
        var controller = Build(new FakeAuthoring());

        Assert.IsType<BadRequestObjectResult>(await controller.EnsureCategory(new TermRequest("  "), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.EnsureTag(new TermRequest("MCP"), CancellationToken.None));
    }
}
