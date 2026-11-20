using Adventures.Identity.WebApi;
using System.Reflection;
using Adventures.Entities;
using Adventures.WebApi;

using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>T3: a role is defined once for the whole platform, so changing what it allows needs the platform right, never a tenant right. Reading the roles stays a tenant right.</summary>
public sealed class RolePlatformRightTests
{
    private static string? RightOf(string action) =>
        typeof(RolesController).GetMethod(action)!.GetCustomAttribute<RequireRightAttribute>()?.Right;

    [Fact]
    public void ChangingWhatARoleAllowsNeedsThePlatformRight()
    {
        Assert.Equal(Rights.PlatformManage, RightOf(nameof(RolesController.SetRights)));
    }

    [Fact]
    public void ReadingTheRolesStillNeedsOnlyTheTenantRolesRight()
    {
        Assert.Equal(Rights.RolesManage, typeof(RolesController).GetCustomAttribute<RequireRightAttribute>()!.Right);
        Assert.Null(RightOf(nameof(RolesController.List)));
        Assert.Null(RightOf(nameof(RolesController.Get)));
    }
}
