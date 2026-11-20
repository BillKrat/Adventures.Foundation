using Adventures.Identity.WebApi;
using System.Security.Claims;
using Adventures.Common.Exceptions;
using Adventures.Entities;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>Editing the rights of a role: the signed-in person is the acting user, refusals are 409, an unknown role is 404.</summary>
public sealed class RolesControllerTests
{
    private sealed class FakeEditor : IRoleRightsEditor
    {
        public string? Result { get; set; } = "admin.access";
        public Exception? Throw { get; set; }
        public (string RoleId, string[] Rights, string Actor)? Last { get; private set; }

        public Task<string?> SetRightsAsync(string roleId, IReadOnlyCollection<string> rights, string actingUserName, CancellationToken cancellationToken = default)
        {
            Last = (roleId, rights.ToArray(), actingUserName);
            return Throw is null ? Task.FromResult(Result) : throw Throw;
        }
    }

    private static (RolesController Controller, FakeEditor Editor) Build()
    {
        var editor = new FakeEditor();
        var controller = new RolesController(null!, editor)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "pat")], "test")) },
            },
        };
        return (controller, editor);
    }

    [Fact]
    public async Task SettingRights_PassesTheSignedInPersonAsTheActor_AndReturnsTheStoredRights()
    {
        var (controller, editor) = Build();

        var result = await controller.SetRights("r1", new RoleRightsRequest(["admin.access"]), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(("r1", new[] { "admin.access" }, "pat"), editor.Last);
        Assert.Equal(new[] { "admin.access" }, Assert.IsType<RoleRightsResponse>(ok.Value).Rights);
    }

    [Fact]
    public async Task AnUnknownRoleIs404_AndARefusedChangeIs409()
    {
        var (controller, editor) = Build();
        editor.Result = null;
        Assert.IsType<NotFoundResult>(await controller.SetRights("nope", new RoleRightsRequest([]), default));

        editor.Throw = new BusinessRuleException("nobody");
        Assert.IsType<ConflictObjectResult>(await controller.SetRights("r1", new RoleRightsRequest([]), default));
    }

    [Fact]
    public void TheCatalogListsEveryRoleRightWithItsDescription_ButNotThePlatformRight()
    {
        var (controller, _) = Build();

        var catalog = Assert.IsAssignableFrom<IEnumerable<Right>>(Assert.IsType<OkObjectResult>(controller.RightsCatalog()).Value).ToList();

        Assert.Equal(Rights.Catalog.Count - 1, catalog.Count);
        Assert.DoesNotContain(catalog, right => right.Name == Rights.PlatformManage);
    }
}
