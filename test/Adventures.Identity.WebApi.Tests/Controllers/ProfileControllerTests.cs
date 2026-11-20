using Adventures.Identity.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Adventures.Entities;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

public class ProfileControllerTests
{
    private const string UserId = "user-1";

    private static EntityFormModel BuildForm(string userName = "BillKrat", string email = "bill@adventuresontheedge.net") =>
        new(
            new EntitySchemaModel("https://global-webnet.com/schema/User",
            [
                new EntityFieldSchema("UserName", "String", IsRequired: true),
                new EntityFieldSchema("Email", "String", IsRequired: false),
            ]),
            new EntityDataModel(UserId, new Dictionary<string, string?>
            {
                ["UserName"] = userName,
                ["Email"] = email,
            }));

    [Fact]
    public async Task Me_ReturnsForm_WhenCallerIsAuthenticated()
    {
        var presenter = new FakeUserPresenter { FormByUserName = BuildForm() };
        var controller = BuildController(presenter, userName: "BillKrat");

        var result = await controller.Me(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var form = Assert.IsType<EntityFormModel>(okResult.Value);
        Assert.Equal("BillKrat", form.Entity.Values["UserName"]);
    }

    [Fact]
    public async Task Me_ReturnsUnauthorized_WhenNoNameClaimIsPresent()
    {
        var presenter = new FakeUserPresenter { FormByUserName = BuildForm() };
        var controller = BuildController(presenter, userName: null);

        var result = await controller.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Me_ReturnsNotFound_WhenNoMatchingUserExists()
    {
        var presenter = new FakeUserPresenter { FormByUserName = null };
        var controller = BuildController(presenter, userName: "BillKrat");

        var result = await controller.Me(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateMe_ReturnsUpdatedForm()
    {
        var presenter = new FakeUserPresenter
        {
            FormByUserName = BuildForm(),
            UpdateResult = BuildForm(email: "new@example.com"),
        };
        var controller = BuildController(presenter, userName: "BillKrat");
        var request = new EntityDataModel(UserId, new Dictionary<string, string?> { ["Email"] = "new@example.com" });

        var result = await controller.UpdateMe(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var form = Assert.IsType<EntityFormModel>(okResult.Value);
        Assert.Equal("new@example.com", form.Entity.Values["Email"]);
        Assert.Same(request, presenter.LastUpdateRequest);
    }

    [Fact]
    public async Task DeleteMe_ReturnsConflict_BecauseOfTheOwnAccountGuard()
    {
        var presenter = new FakeUserPresenter { FormByUserName = BuildForm(), ThrowOnDelete = true };
        var controller = BuildController(presenter, userName: "BillKrat");

        var result = await controller.DeleteMe(CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    private static ProfileController BuildController(IUserPresenter presenter, string? userName)
    {
        var identity = userName is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], authenticationType: "Test");

        return new ProfileController(presenter)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }
}
