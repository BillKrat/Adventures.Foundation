using Adventures.Identity.WebApi;
using System.Security.Claims;
using Adventures.Entities;
using Adventures.WebApi;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// The platform administrator switch on a person (2026-11-08): visible to a tenant administrator, changed only by someone who holds the platform right, never leaving the platform
/// without an administrator, and only for people of the tenant being administered. Threat addressed: a tenant administrator granting themselves platform power.
/// </summary>
public sealed class UsersPlatformSwitchTests
{
    private sealed class Switches(params string[] on) : IPlatformAdministrators
    {
        public HashSet<string> On { get; } = [.. on];

        public Task<bool> IsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(On.Contains(userId));

        public Task<IReadOnlySet<string>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(On.ToHashSet());

        public Task SetAsync(string userId, bool on, string changedBy, CancellationToken cancellationToken = default)
        {
            _ = on ? On.Add(userId) : On.Remove(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class People : IUserBll
    {
        public Task<User?> GetAsync(string entityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(entityId is "user-bill" or "user-claude" ? new User(UsersControllerTests.UserSchemaWithRelation) : null);

        public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);

        public Task<User> CreateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> CreateInTenantAsync(User entity, IReadOnlySet<string> tenantPeopleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Caller(bool mayChange) : IRightsAuthorizer
    {
        public Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default) =>
            Task.FromResult(mayChange && right == Rights.PlatformManage);
    }

    private static UsersController Controller() => UsersControllerTests.BuildController(new FakeUserPresenter(), "Admin");

    [Fact]
    public async Task TheSwitchIsReported_WithWhetherTheCallerMayChangeIt()
    {
        var switches = new Switches("user-bill");

        var asPlatform = Assert.IsType<OkObjectResult>(await Controller().GetPlatform("user-bill", switches, new Caller(true), CancellationToken.None));
        Assert.Equal(new PlatformState(true, true), asPlatform.Value);

        var asTenantAdmin = Assert.IsType<OkObjectResult>(await Controller().GetPlatform("user-claude", switches, new Caller(false), CancellationToken.None));
        Assert.Equal(new PlatformState(false, false), asTenantAdmin.Value);
    }

    [Fact]
    public async Task APersonOutsideTheTenantIsNotFound_ForReadingAndChanging()
    {
        var controller = UsersControllerTests.BuildController(new FakeUserPresenter(), "Admin", membership: new FakeMembership { Members = ["user-bill"] });
        var switches = new Switches("user-bill");

        Assert.IsType<NotFoundResult>(await controller.GetPlatform("user-claude", switches, new Caller(true), CancellationToken.None));
        Assert.IsType<NotFoundResult>(
            await controller.SetPlatform("user-claude", new PlatformRequest(true), new PlatformAdministratorBll(switches, new People()), CancellationToken.None));
        Assert.DoesNotContain("user-claude", switches.On);
    }

    [Fact]
    public void ChangingTheSwitchNeedsThePlatformRight_NotOnlyTheUsersRight()
    {
        var guard = Assert.Single(
            typeof(UsersController).GetMethod(nameof(UsersController.SetPlatform))!.GetCustomAttributes(typeof(RequireRightAttribute), inherit: true).Cast<RequireRightAttribute>());

        Assert.Equal(Rights.PlatformManage, guard.Right);
    }

    [Fact]
    public async Task APlatformAdministratorSwitchesSomeoneOn_AndOffAgain()
    {
        var switches = new Switches("user-bill");
        var bll = new PlatformAdministratorBll(switches, new People());

        Assert.IsType<NoContentResult>(await Controller().SetPlatform("user-claude", new PlatformRequest(true), bll, CancellationToken.None));
        Assert.Contains("user-claude", switches.On);

        Assert.IsType<NoContentResult>(await Controller().SetPlatform("user-claude", new PlatformRequest(false), bll, CancellationToken.None));
        Assert.DoesNotContain("user-claude", switches.On);
    }

    [Fact]
    public async Task TheLastPlatformAdministratorCannotBeSwitchedOff_AnswerIsAConflict()
    {
        var switches = new Switches("user-bill");
        var bll = new PlatformAdministratorBll(switches, new People());

        var result = await Controller().SetPlatform("user-bill", new PlatformRequest(false), bll, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Contains("user-bill", switches.On);
    }
}
