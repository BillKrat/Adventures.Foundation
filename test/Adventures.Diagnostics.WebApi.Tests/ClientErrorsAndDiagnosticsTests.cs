using Adventures.Diagnostics.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Adventures.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventures.Diagnostics.WebApi.Tests;

public sealed class ClientErrorsAndDiagnosticsTests
{
    private static ClientErrorsController NewController(RecordingExceptionLog log, bool signedIn = true, bool reset = true)
    {
        if (reset)
        {
            ClientErrorsController.ResetForTests();
        }

        var controller = new ClientErrorsController(log, NullLogger<ClientErrorsController>.Instance);
        var context = new DefaultHttpContext
        {
            User = signedIn ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin")], "Bearer")) : new ClaimsPrincipal(new ClaimsIdentity()),
        };
        context.Items[Adventures.WebApi.CorrelationIdMiddleware.ItemsKey] = "browser-1";
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    [Fact]
    public async Task Report_RecordsTheClientErrorThrottled_WithTheCorrelationId()
    {
        var log = new RecordingExceptionLog("e-1");

        var result = await NewController(log).Report(new ClientErrorReport("TypeError: x", "at f (a.js:1:1)", "/profile"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var recorded = Assert.Single(log.Throttled);
        Assert.IsType<ClientReportedException>(recorded.Exception);
        Assert.Equal("client: /profile", recorded.Request);
        Assert.Equal("browser-1", recorded.CorrelationId);
        Assert.Equal(ClientErrorsController.SameErrorWindow, recorded.Window);
    }

    [Fact]
    public async Task Report_RejectsAnEmptyMessage()
    {
        var log = new RecordingExceptionLog("e-1");

        var result = await NewController(log).Report(new ClientErrorReport("  ", null, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(log.Throttled);
    }

    [Fact]
    public async Task Report_FromAnAnonymousCaller_IsRecordedWithoutAUserName()
    {
        var log = new RecordingExceptionLog("e-1");

        var result = await NewController(log, signedIn: false).Report(new ClientErrorReport("TypeError: login", null, "/login"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Single(log.Throttled);
        Assert.Null(log.LastUserName);
    }

    [Fact]
    public async Task Report_AnonymousCallers_AreCappedPerMinute_SignedInOnesHaveTheirOwnCap()
    {
        var log = new RecordingExceptionLog("e-1");
        var controller = NewController(log, signedIn: false);
        for (var i = 0; i < ClientErrorsController.MaxPerMinuteAnonymous; i++)
        {
            Assert.IsType<AcceptedResult>(await controller.Report(new ClientErrorReport($"e{i}", null, "/login"), CancellationToken.None));
        }

        var refused = Assert.IsType<StatusCodeResult>(await controller.Report(new ClientErrorReport("one too many", null, "/login"), CancellationToken.None));
        Assert.Equal(429, refused.StatusCode);

        var signedIn = NewController(log, signedIn: true, reset: false);
        Assert.IsType<AcceptedResult>(await signedIn.Report(new ClientErrorReport("still fine", null, "/profile"), CancellationToken.None));
    }

    [Fact]
    public void ClientErrors_AreOpenToAnonymous_AndDiagnostics_RequireAnAdministrator()
    {
        Assert.NotEmpty(typeof(ClientErrorsController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        var admin = Assert.Single(typeof(AdminDiagnosticsController).GetCustomAttributes(typeof(Adventures.WebApi.RequireRightAttribute), true).Cast<Adventures.WebApi.RequireRightAttribute>());
        Assert.Equal(Adventures.Entities.Rights.DiagnosticsRead, admin.Right);
        var machine = Assert.Single(typeof(McpDiagnosticsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Scope:mcp.diagnostics.read", machine.Policy);
    }
}
