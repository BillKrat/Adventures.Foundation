using System.Security.Claims;
using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// The tenant switcher on the server (2026-11-08): the client names a tenant in a header and the server decides whether the signed-in person may act there. Threat addressed: acting in,
/// or reading the data of, a tenant one does not belong to (the header is never trusted on its own).
/// </summary>
public sealed class ActingTenantTests
{
    private static readonly TenantHostInfo Mine = new("t-mine", "Mine", "mine.test", []);
    private static readonly TenantHostInfo Theirs = new("t-theirs", "Theirs", "theirs.test", []);

    private sealed class Acting(params TenantHostInfo[] allowed) : IActingTenants
    {
        public Task<IReadOnlyList<TenantHostInfo>> ForAsync(string userName, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantHostInfo>>(allowed);

        public Task<TenantHostInfo?> AllowedAsync(string userName, string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(allowed.FirstOrDefault(t => t.Id == tenantId));
    }

    private static async Task<(bool Reached, int Status, CurrentTenant Current)> RunAsync(string? header, bool signedIn, params TenantHostInfo[] allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("api.mine.test");
        if (header is not null)
        {
            context.Request.Headers[ActingTenantMiddleware.HeaderName] = header;
        }

        context.User = signedIn ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ann")], "Bearer")) : new ClaimsPrincipal(new ClaimsIdentity());
        var holder = new RequestTenant();
        holder.Set(new CurrentTenant(Mine.Id, Mine.Name, Mine.Domain, "api.mine.test", TenantMatch.Domain));
        var reached = false;

        await new ActingTenantMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context, holder, new Acting(allowed));
        return (reached, context.Response.StatusCode, holder.Current);
    }

    [Fact]
    public async Task APersonAllowedInAnotherTenantWorksThere_AndTheHostIsKept()
    {
        var (reached, _, current) = await RunAsync(Theirs.Id, signedIn: true, Mine, Theirs);

        Assert.True(reached);
        Assert.Equal(Theirs.Id, current.Id);
        Assert.Equal(Theirs.Domain, current.Domain);
        Assert.Equal(TenantMatch.Acting, current.Match);
        Assert.Equal("api.mine.test", current.Host);
    }

    [Fact]
    public async Task APersonNotAllowedInTheTenantIsRefusedWith403_AndNothingRuns()
    {
        var (reached, status, current) = await RunAsync(Theirs.Id, signedIn: true, Mine);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal(Mine.Id, current.Id);
    }

    [Fact]
    public async Task AnUnknownTenantIsRefusedLikeANotAllowedOne()
    {
        Assert.Equal(StatusCodes.Status403Forbidden, (await RunAsync("not-a-tenant", signedIn: true, Mine, Theirs)).Status);
    }

    [Fact]
    public async Task AnAnonymousCallerNeverChoosesATenant_TheHeaderIsIgnored()
    {
        var (reached, _, current) = await RunAsync(Theirs.Id, signedIn: false, Mine, Theirs);

        Assert.True(reached);
        Assert.Equal(Mine.Id, current.Id);
        Assert.Equal(TenantMatch.Domain, current.Match);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t-mine")]
    public async Task NoHeaderOrTheHostsOwnTenantChangesNothing(string? header)
    {
        var (reached, _, current) = await RunAsync(header, signedIn: true, Mine, Theirs);

        Assert.True(reached);
        Assert.Equal(TenantMatch.Domain, current.Match);
    }
}
