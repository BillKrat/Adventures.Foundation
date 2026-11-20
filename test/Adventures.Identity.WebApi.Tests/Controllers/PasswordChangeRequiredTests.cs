using System.Security.Claims;
using Adventures.Identity;
using Adventures.Identity.WebApi;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// A person who signed in with a password that must be changed can do nothing but change it: every other API call is refused with 403 and a code the
/// client acts on, until the store says the change is done (no second sign-in needed). Threat addressed: a seeded or administrator-set default password
/// working like any other.
/// </summary>
public sealed class PasswordChangeRequiredTests
{
    private sealed class FakeGate(bool pending) : IPasswordChangeGate
    {
        public int Asked;

        public Task<bool> IsPendingAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(pending);
        }
    }

    private static DefaultHttpContext Context(string path, bool signedIn = true, bool marked = true, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        if (signedIn)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Name, "admin") };
            if (marked)
            {
                claims.Add(new Claim(WellKnownClaims.PasswordChange, "true"));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return context;
    }

    private static async Task<bool> PassesThrough(DefaultHttpContext context, FakeGate? gate)
    {
        var reached = false;
        var services = new ServiceCollection();
        if (gate is not null)
        {
            services.AddSingleton<IPasswordChangeGate>(gate);
        }

        context.RequestServices = services.BuildServiceProvider();
        await new PasswordChangeRequiredMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context);
        return reached;
    }

    [Theory]
    [InlineData("/api/admin/posts")]
    [InlineData("/api/admin/menu")]
    [InlineData("/api/users")]
    [InlineData("/api/profile")]
    [InlineData("/api/authx/token")]
    [InlineData("/API/Admin/Posts")]
    public async Task APendingChange_RefusesEveryOtherApiCall_With403AndACode(string path)
    {
        var context = Context(path);

        Assert.False(await PassesThrough(context, new FakeGate(pending: true)));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains(PasswordChangeRequiredMiddleware.Code, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/auth/change-password")]
    [InlineData("/api/auth/whoami")]
    [InlineData("/api/auth/rights")]
    [InlineData("/api/auth/token")]
    [InlineData("/api/profile/me")]
    [InlineData("/api/public/feed")]
    [InlineData("/api/runtime")]
    [InlineData("/api/health")]
    [InlineData("/Api/Auth/Change-Password")]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task APendingChange_StillAllowsChangingIt_TheProfileThePublicSiteAndHealth(string path)
    {
        Assert.True(await PassesThrough(Context(path), new FakeGate(pending: true)));
    }

    [Fact]
    public async Task OnceTheChangeIsDone_TheSameTokenWorksAgain()
    {
        var gate = new FakeGate(pending: false);

        Assert.True(await PassesThrough(Context("/api/admin/posts"), gate));
        Assert.Equal(1, gate.Asked);
    }

    [Fact]
    public async Task AnUnmarkedToken_OrAnAnonymousCaller_IsNeverHeldUp_AndTheStoreIsNotAsked()
    {
        var gate = new FakeGate(pending: true);

        Assert.True(await PassesThrough(Context("/api/admin/posts", marked: false), gate));
        Assert.True(await PassesThrough(Context("/api/admin/posts", signedIn: false), gate));
        Assert.Equal(0, gate.Asked);
    }

    [Fact]
    public async Task AMarkedTokenOnAHostWithNoGate_IsRefused_NotWavedThrough()
    {
        var context = Context("/api/admin/posts");

        Assert.False(await PassesThrough(context, gate: null));
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task AMarkedTokenWithABrokenSubject_IsRefused_NotWavedThrough()
    {
        var context = Context("/api/admin/posts");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(WellKnownClaims.PasswordChange, "true")], "test"));

        Assert.False(await PassesThrough(context, new FakeGate(pending: false)));
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }
}
