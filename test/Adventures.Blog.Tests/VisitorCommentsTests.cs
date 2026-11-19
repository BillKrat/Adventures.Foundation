using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>Visitor comments: pending until a person approves, only approved ones are public, and the rules that keep an anonymous endpoint safe.</summary>
public sealed class VisitorCommentsTests : IAsyncLifetime
{
    private IVisitorComments _comments = null!;
    private readonly FakePosts _posts = new();

    private sealed class FakePosts : ICommentablePosts
    {
        public Dictionary<string, CommentablePost> Posts { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["open-post"] = new("An open post", true),
            ["closed-post"] = new("A closed post", false),
        };

        public Task<CommentablePost?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Posts.TryGetValue(slug, out var post) ? post : null);
    }

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-021-visitor-comments.nq"));
        var quads = await store.QueryAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, BlogConstants.Content.VisitorCommentSchemaIri), BlogConstants.Content.VisitorCommentBaseIri,
            BlogConstants.Content.VisitorCommentTypeIri, BlogConstants.Content.DefaultGraph, s => new VisitorComment(s));
        services.AddScoped<IEntityBll<VisitorComment>>(p => new EntityBll<VisitorComment>(new EntityDal<VisitorComment>(p.GetRequiredService<IEntityRepository<VisitorComment>>())));
        services.AddSingleton<ICommentablePosts>(_posts);
        services.AddScoped<IVisitorComments, VisitorComments>();
        _comments = services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IVisitorComments>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SubmittedComment Valid(string body = "Nice post.") => new("open-post", "Pat", "pat@example.com", body);

    [Fact]
    public async Task ASubmittedCommentWaitsForApproval_AndIsNotPublic()
    {
        await _comments.SubmitAsync(Valid());

        Assert.Empty(await _comments.ListApprovedAsync("open-post"));
        Assert.Equal(1, await _comments.CountPendingAsync());
        var waiting = Assert.Single(await _comments.ListAsync(CommentStatuses.Pending));
        Assert.Equal("An open post", waiting.PostTitle);
        Assert.Equal("Pat", waiting.Author);
    }

    [Fact]
    public async Task AnApprovedCommentIsPublic_WithoutTheEmailAddress_AndLeavesThePendingCount()
    {
        await _comments.SubmitAsync(Valid());
        var id = (await _comments.ListAsync(CommentStatuses.Pending)).Single().Id;

        Assert.Equal(1, await _comments.SetStatusAsync([id], CommentStatuses.Approved));

        var shown = Assert.Single(await _comments.ListApprovedAsync("open-post"));
        Assert.Equal("Pat", shown.Author);
        Assert.Equal("Nice post.", shown.Body);
        Assert.DoesNotContain("example.com", System.Text.Json.JsonSerializer.Serialize(shown));
        Assert.Equal(0, await _comments.CountPendingAsync());
        Assert.Empty(await _comments.ListApprovedAsync("closed-post"));
    }

    [Fact]
    public async Task UnapprovingTakesACommentBackOffTheSite()
    {
        await _comments.SubmitAsync(Valid());
        var id = (await _comments.ListAsync(null)).Single().Id;
        await _comments.SetStatusAsync([id], CommentStatuses.Approved);

        await _comments.SetStatusAsync([id], CommentStatuses.Pending);

        Assert.Empty(await _comments.ListApprovedAsync("open-post"));
    }

    [Fact]
    public async Task DeletingRemovesTheCommentEverywhere()
    {
        await _comments.SubmitAsync(Valid());
        var id = (await _comments.ListAsync(null)).Single().Id;

        Assert.Equal(1, await _comments.DeleteAsync([id]));

        Assert.Empty(await _comments.ListAsync(null));
    }

    [Fact]
    public async Task ModerationFilters_ByStatus()
    {
        await _comments.SubmitAsync(Valid("first"));
        await _comments.SubmitAsync(Valid("second"));
        var first = (await _comments.ListAsync(null)).First(c => c.Body == "first").Id;
        await _comments.SetStatusAsync([first], CommentStatuses.Approved);

        Assert.Equal(["first"], (await _comments.ListAsync(CommentStatuses.Approved)).Select(c => c.Body));
        Assert.Equal(["second"], (await _comments.ListAsync(CommentStatuses.Pending)).Select(c => c.Body));
        Assert.Equal(2, (await _comments.ListAsync(null)).Count);
    }

    [Fact]
    public async Task AnUnknownStatusIsRefused()
    {
        await _comments.SubmitAsync(Valid());
        var id = (await _comments.ListAsync(null)).Single().Id;

        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SetStatusAsync([id], "Published"));
    }

    [Theory]
    [InlineData("missing-post")]
    [InlineData("closed-post")]
    public async Task CommentsAreRefusedOnAPostThatIsMissingOrHasCommentsOff(string slug)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SubmitAsync(Valid() with { PostSlug = slug }));

        Assert.Empty(await _comments.ListAsync(null));
    }

    [Theory]
    [InlineData("", "text")]
    [InlineData("   ", "text")]
    [InlineData("Pat", "")]
    [InlineData("Pat", "   ")]
    public async Task AnEmptyNameOrTextIsRefused(string author, string body)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SubmitAsync(Valid() with { Author = author, Body = body }));
    }

    [Fact]
    public async Task TooLongTextOrNameIsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SubmitAsync(Valid(new string('x', VisitorComments.MaxBodyLength + 1))));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SubmitAsync(Valid() with { Author = new string('a', VisitorComments.MaxAuthorLength + 1) }));
    }

    [Fact]
    public async Task AMalformedEmailIsRefused_AndAMissingOneIsFine()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SubmitAsync(Valid() with { Email = "not-an-address" }));

        await _comments.SubmitAsync(Valid() with { Email = null });
        Assert.Equal(1, await _comments.CountPendingAsync());
    }

    [Fact]
    public async Task ControlCharactersAreStripped_AndTextIsStoredAsPlainText()
    {
        await _comments.SubmitAsync(Valid("<script>alert(1)</script>\u0000 hi\u0007"));

        var stored = (await _comments.ListAsync(null)).Single();
        Assert.Equal("<script>alert(1)</script> hi", stored.Body);
    }

    [Fact]
    public async Task TheQueueOfWaitingCommentsIsCapped_SoAFloodCannotFillTheStore()
    {
        for (var i = 0; i < VisitorComments.MaxPending; i++)
        {
            await _comments.SubmitAsync(Valid($"comment {i}"));
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => _comments.SubmitAsync(Valid("one too many")));
    }
}
