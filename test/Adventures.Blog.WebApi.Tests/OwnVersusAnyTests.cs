using System.Security.Claims;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>
/// S2b. A right named ".own" must mean something: a person holding only posts.edit.own / posts.delete.own may change or delete a post they wrote and
/// no one else's, and changing a page's published flag needs pages.publish, not just pages.edit. Threat: privilege escalation inside the editor role family.
/// </summary>
public sealed class OwnVersusAnyTests
{
    private sealed class HeldRights(params string[] held) : IRightsAuthorizer
    {
        public Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(held.ToHashSet());

        public Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default) =>
            Task.FromResult(held.Contains(right));
    }

    private sealed class FakePresenter<T>(EntityFormModel? stored) : IEntityPresenter<T> where T : DynamicEntity
    {
        public int Updates { get; private set; }

        public int Deletes { get; private set; }

        public Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(stored);

        public Task<EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default) => Task.FromResult<EntityFormModel?>(null);

        public Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntityDataModel>>([]);

        public Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default)
        {
            Updates++;
            return Task.FromResult(stored);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            Deletes++;
            return Task.FromResult(true);
        }
    }

    private static EntityFormModel Form(string id, params (string Key, string Value)[] values) =>
        new(new EntitySchemaModel("urn:t", []), new EntityDataModel(id, values.ToDictionary(v => v.Key, v => (string?)v.Value)));

    private static void Sign(ControllerBase controller, string name) =>
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test")) },
        };

    private static (PostsController Controller, FakePresenter<Post> Presenter) Posts(string author, params string[] rights)
    {
        var presenter = new FakePresenter<Post>(Form("p1", ("Author", author), ("Title", "T")));
        var users = new FakeUserPresenter { FormByUserName = Form("u-alice", ("UserName", "alice")) };
        var controller = new PostsController(presenter, null!, users, new HeldRights(rights));
        Sign(controller, "alice");
        return (controller, presenter);
    }

    private static readonly EntityDataModel Edit = new("p1", new Dictionary<string, string?> { ["Title"] = "New" });

    [Fact]
    public async Task AnAuthorWithOnlyEditOwn_ChangesTheirOwnPost()
    {
        var (controller, presenter) = Posts("u-alice", Rights.PostsEditOwn);

        var result = await controller.Update("p1", Edit, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, presenter.Updates);
    }

    [Fact]
    public async Task AnAuthorWithOnlyEditOwn_IsRefusedAnotherPersonsPost()
    {
        var (controller, presenter) = Posts("u-bob", Rights.PostsEditOwn);

        var result = await controller.Update("p1", Edit, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
        Assert.Equal(0, presenter.Updates);
    }

    [Fact]
    public async Task EditAny_ChangesAnyonesPost()
    {
        var (controller, presenter) = Posts("u-bob", Rights.PostsEditAny);

        var result = await controller.Update("p1", Edit, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, presenter.Updates);
    }

    [Fact]
    public async Task DeleteOwn_DeletesOnlyYourOwnPost_AndDeleteAnyDeletesAnyones()
    {
        var (mine, minePresenter) = Posts("u-alice", Rights.PostsDeleteOwn);
        var (theirs, theirsPresenter) = Posts("u-bob", Rights.PostsDeleteOwn);
        var (any, anyPresenter) = Posts("u-bob", Rights.PostsDeleteAny);

        await mine.Delete("p1", CancellationToken.None);
        var refused = await theirs.Delete("p1", CancellationToken.None);
        await any.Delete("p1", CancellationToken.None);

        Assert.Equal(1, minePresenter.Deletes);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(refused).StatusCode);
        Assert.Equal(0, theirsPresenter.Deletes);
        Assert.Equal(1, anyPresenter.Deletes);
    }

    private static (PagesController Controller, FakePresenter<Page> Presenter) Pages(string storedPublished, params string[] rights)
    {
        var presenter = new FakePresenter<Page>(Form("g1", ("Title", "About"), ("IsPublished", storedPublished)));
        var controller = new PagesController(presenter, new HeldRights(rights));
        Sign(controller, "alice");
        return (controller, presenter);
    }

    [Fact]
    public async Task PublishingAPage_NeedsPagesPublish_NotJustPagesEdit()
    {
        var (controller, presenter) = Pages("false", Rights.PagesEdit);
        var publish = new EntityDataModel("g1", new Dictionary<string, string?> { ["Title"] = "About", ["IsPublished"] = "true" });

        var result = await controller.Update("g1", publish, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
        Assert.Equal(0, presenter.Updates);
    }

    [Fact]
    public async Task EditingAPageWithoutTouchingThePublishedFlag_NeedsOnlyPagesEdit()
    {
        var (controller, presenter) = Pages("true", Rights.PagesEdit);
        var edit = new EntityDataModel("g1", new Dictionary<string, string?> { ["Title"] = "About us", ["IsPublished"] = "true" });

        var result = await controller.Update("g1", edit, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, presenter.Updates);
    }

    [Fact]
    public async Task PagesPublish_MayPublishAndUnpublish()
    {
        var (controller, presenter) = Pages("false", Rights.PagesEdit, Rights.PagesPublish);
        var publish = new EntityDataModel("g1", new Dictionary<string, string?> { ["IsPublished"] = "true" });

        await controller.Update("g1", publish, CancellationToken.None);

        Assert.Equal(1, presenter.Updates);
    }
}
