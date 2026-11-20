using Adventures.Identity.WebApi;
using Adventures.Entities;
using Adventures.WebApi;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// T1: the tenant shown is the one the server decided from the host it saw (<see cref="IRequestTenant"/>). It used to come from the browser's Origin or
/// Referer, which the caller sets, so it could not be trusted for anything but display; it is now the same trusted tenant everything else uses.
/// </summary>
public sealed class TenantControllerTests
{
    private sealed class FixedTenant(CurrentTenant current) : IRequestTenant
    {
        public CurrentTenant Current { get; } = current;
    }

    private static TenantController Build(CurrentTenant current, string? origin = null, string? referer = null)
    {
        var controller = new TenantController(new FixedTenant(current));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("api.global-webnet.com");
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        if (referer is not null)
        {
            context.Request.Headers.Referer = referer;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static object? Json(IActionResult result, string name)
    {
        var value = Assert.IsType<OkObjectResult>(result).Value!;
        return value.GetType().GetProperty(name)!.GetValue(value);
    }

    [Fact]
    public void ReportsTheTenantTheServerResolvedFromTheHost()
    {
        var controller = Build(new CurrentTenant("t1", "DEV Main", "global-webnet.com", "api.global-webnet.com", TenantMatch.Domain));

        var result = controller.Current();

        Assert.Equal("api.global-webnet.com", Json(result, "Host"));
        Assert.Equal("Domain", Json(result, "Match"));
        var tenant = Json(result, "Tenant")!;
        Assert.Equal("global-webnet.com", tenant.GetType().GetProperty("Domain")!.GetValue(tenant));
    }

    [Fact]
    public void AnOriginOrRefererNamingAnotherTenantChangesNothing()
    {
        var controller = Build(
            new CurrentTenant("t1", "DEV Main", "global-webnet.com", "api.global-webnet.com", TenantMatch.Domain),
            origin: "https://www.adventuresedge.net",
            referer: "https://adventuresedge.net/admin/users");

        var result = controller.Current();

        Assert.Equal("api.global-webnet.com", Json(result, "Host"));
        var tenant = Json(result, "Tenant")!;
        Assert.Equal("global-webnet.com", tenant.GetType().GetProperty("Domain")!.GetValue(tenant));
    }

    [Fact]
    public void AHostThatMatchesNoTenantSaysSo()
    {
        var controller = Build(new CurrentTenant(null, null, null, "unbound.example", TenantMatch.None));

        var result = controller.Current();

        Assert.Equal("None", Json(result, "Match"));
        Assert.Null(Json(result, "Tenant"));
    }
}
