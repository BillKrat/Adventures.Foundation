using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

public class CommentsControllersTests
{
    private sealed class FakeSite(bool moderate = true) : ISiteSettings
    {
        public Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicSiteSettings("Site", string.Empty, 10, CommentsModerate: moderate));

        public Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
    }

    [Fact]
    public async Task TheAnswerTellsTheTruth_WaitingForApprovalWhenModerated_PublishedWhenNot()
    {
        var moderated = Assert.IsType<AcceptedResult>(await new PublicCommentsController(new FakeComments(), new FakeSite(moderate: true))
            .Submit("a-post", new CommentRequest("Pat", null, "Nice", null), CancellationToken.None));
        var open = Assert.IsType<AcceptedResult>(await new PublicCommentsController(new FakeComments(), new FakeSite(moderate: false))
            .Submit("a-post", new CommentRequest("Pat", null, "Nice", null), CancellationToken.None));

        Assert.Contains("approved", Assert.IsType<CommentAccepted>(moderated.Value).Message);
        Assert.DoesNotContain("approved", Assert.IsType<CommentAccepted>(open.Value).Message);
        Assert.Contains("published", Assert.IsType<CommentAccepted>(open.Value).Message);
    }

    private sealed class FakeComments : IVisitorComments
    {
        public List<SubmittedComment> Submitted { get; } = [];
        public Exception? Refuse { get; set; }
        public (IReadOnlyCollection<string> Ids, string Status)? Set { get; private set; }
        public IReadOnlyCollection<string>? Deleted { get; private set; }

        public Task SubmitAsync(SubmittedComment comment, CancellationToken cancellationToken = default)
        {
            if (Refuse is not null)
            {
                throw Refuse;
            }

            Submitted.Add(comment);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PublicComment>> ListApprovedAsync(string postSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicComment>>([new PublicComment("Pat", "2026-11-19T00:00:00Z", "hello")]);

        public Task<IReadOnlyList<ModerationComment>> ListAsync(string? status, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModerationComment>>([]);

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(3);

        public Task<int> SetStatusAsync(IReadOnlyCollection<string> ids, string status, CancellationToken cancellationToken = default)
        {
            Set = (ids, status);
            return Task.FromResult(ids.Count);
        }

        public Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        {
            Deleted = ids;
            return Task.FromResult(ids.Count);
        }
    }

    [Fact]
    public async Task AFilledHoneypotIsAnsweredLikeARealCommentAndStoresNothing()
    {
        var fake = new FakeComments();

        var result = await new PublicCommentsController(fake, new FakeSite()).Submit("a-post", new CommentRequest("Bot", null, "buy now", "http://spam.example"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(fake.Submitted);
    }

    [Fact]
    public async Task ARealCommentIsSubmittedForTheAddressInTheRoute_WithNoWayToChooseItsStatus()
    {
        var fake = new FakeComments();

        var result = await new PublicCommentsController(fake, new FakeSite()).Submit("a-post", new CommentRequest("Pat", "pat@example.com", "Nice", null), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(new SubmittedComment("a-post", "Pat", "pat@example.com", "Nice"), Assert.Single(fake.Submitted));
        Assert.DoesNotContain(typeof(CommentRequest).GetProperties(), p => p.Name.Contains("Status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARefusedCommentIsAConflictWithTheMessage()
    {
        var fake = new FakeComments { Refuse = new BusinessRuleException("Comments are not open on this post.") };

        var result = await new PublicCommentsController(fake, new FakeSite()).Submit("a-post", new CommentRequest("Pat", null, "Nice", null), CancellationToken.None);

        Assert.Equal("Comments are not open on this post.", Assert.IsType<ConflictObjectResult>(result).Value);
    }

    [Fact]
    public void TheSubmitEndpointIsRateLimitedAndTheModerationRoutesNeedTheRight()
    {
        var submit = typeof(PublicCommentsController).GetMethod(nameof(PublicCommentsController.Submit))!;
        Assert.Equal(RequestHardening.CommentPolicy, submit.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true).Cast<EnableRateLimitingAttribute>().Single().PolicyName);
        Assert.Equal(Rights.CommentsModerate, typeof(CommentModerationController).GetCustomAttributes(typeof(RequireRightAttribute), inherit: true).Cast<RequireRightAttribute>().Single().Right);
    }

    [Theory]
    [InlineData("approve", "Approved")]
    [InlineData("unapprove", "Pending")]
    public async Task ApproveAndUnapproveSetTheStatus(string action, string status)
    {
        var fake = new FakeComments();

        var result = await new CommentModerationController(fake).Process(new CommentAction(action, ["c1", "c2"]), CancellationToken.None);

        Assert.Equal(2, Assert.IsType<CommentActionResult>(Assert.IsType<OkObjectResult>(result).Value).Changed);
        Assert.Equal(status, fake.Set!.Value.Status);
    }

    [Fact]
    public async Task DeleteRemovesTheComments()
    {
        var fake = new FakeComments();

        await new CommentModerationController(fake).Process(new CommentAction("delete", ["c1"]), CancellationToken.None);

        Assert.Equal(["c1"], fake.Deleted);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData(null)]
    public async Task AnUnknownActionIsABadRequest(string? action)
    {
        var result = await new CommentModerationController(new FakeComments()).Process(new CommentAction(action, ["c1"]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task NoIdsOrTooManyIdsIsABadRequest()
    {
        var controller = new CommentModerationController(new FakeComments());

        Assert.IsType<BadRequestObjectResult>(await controller.Process(new CommentAction("approve", []), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Process(
            new CommentAction("approve", Enumerable.Range(0, CommentModerationController.MaxIdsPerCall + 1).Select(n => n.ToString()).ToList()), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownStatusFilterIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(await new CommentModerationController(new FakeComments()).List("Spam", CancellationToken.None));
    }
}
