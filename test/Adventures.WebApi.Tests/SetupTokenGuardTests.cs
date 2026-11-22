using System.Security.Claims;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// A setup token is issued to a platform administrator on a host that reaches no tenant, to attach that host to a site or start a new one. It must work for the setup API and nothing else: used
/// on any other route (a tenant host, the content API) it would be an authenticated caller whose name could be mistaken for a person of that tenant. Threat addressed: a setup token as a
/// way into another tenant.
/// </summary>
public sealed class SetupTokenGuardTests
{
    private static ClaimsPrincipal Person(bool setup) =>
        new(new ClaimsIdentity(setup ? [new Claim(ClaimTypes.Name, "setup-session"), new Claim(WellKnownClaims.Setup, "platform")] : [new Claim(ClaimTypes.Name, "Admin")], "test"));

    private static async Task<(bool Reached, int Status)> RunAsync(string path, ClaimsPrincipal? user)
    {
        var context = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var reached = false;
        await new SetupTokenGuardMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);
        return (reached, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/posts")]
    [InlineData("/api/auth/rights")]
    [InlineData("/api/tenant/hosts")]
    [InlineData("/api/admin/menu")]
    [InlineData("/api/profile/me")]
    [InlineData("/")]
    [InlineData("/api/setupX")]
    [InlineData("/api/setup-not")]
    public async Task ASetupToken_IsRefusedEverywhereButTheSetupApi(string path)
    {
        var run = await RunAsync(path, Person(setup: true));

        Assert.False(run.Reached);
        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
    }

    [Theory]
    [InlineData("/api/setup/sites")]
    [InlineData("/api/setup/attach")]
    [InlineData("/API/SETUP/new")]
    public async Task ASetupToken_ReachesTheSetupApi(string path)
    {
        var run = await RunAsync(path, Person(setup: true));

        Assert.True(run.Reached);
    }

    [Theory]
    [InlineData("/api/posts")]
    [InlineData("/api/setup/sites")]
    public async Task EveryOtherCaller_IsLeftAlone(string path)
    {
        Assert.True((await RunAsync(path, Person(setup: false))).Reached);
        Assert.True((await RunAsync(path, null)).Reached);
    }
}
