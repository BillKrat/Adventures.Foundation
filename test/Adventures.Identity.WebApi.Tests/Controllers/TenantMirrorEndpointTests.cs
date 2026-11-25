using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.Identity.WebApi;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>The mirror pointer is the platform administrator's (a pointer decides whose data a host serves), and it cannot be set from the address of the tenant it would change.</summary>
public sealed class TenantMirrorEndpointTests
{
    private sealed class Mirrors(Func<string, string?, bool> set) : ITenantMirrors
    {
        public (string Id, string? Target)? Called { get; private set; }

        public Task<bool> SetAsync(string tenantId, string? targetId, CancellationToken cancellationToken = default)
        {
            Called = (tenantId, targetId);
            return Task.FromResult(set(tenantId, targetId));
        }
    }

    private static TenantController On(CurrentTenant current, TenantCacheInvalidator? caches = null) => new(new FixedRequestTenant(current), caches);

    private static CurrentTenant Own(string id) => new(id, "Scratch", "scratch.example", "scratch.example", TenantMatch.Domain);

    private static readonly CurrentTenant OnAnotherSite = new("main-id", "Main", "main.example", "main.example", TenantMatch.Domain);

    [Fact]
    public void TheRoute_NeedsThePlatformRight()
    {
        var guard = Assert.Single(typeof(TenantController).GetMethod(nameof(TenantController.SetMirror))!.GetCustomAttributes(typeof(RequireRightAttribute), inherit: true).Cast<RequireRightAttribute>());

        Assert.Equal(Rights.PlatformManage, guard.Right);
    }

    [Fact]
    public async Task SettingAPointer_FromAnotherSite_IsAccepted_AndClearsTheTenantCaches()
    {
        var invalidator = new TenantCacheInvalidator();
        var before = invalidator.Token;
        var mirrors = new Mirrors((_, _) => true);

        var result = await On(OnAnotherSite, invalidator).SetMirror("scratch-id", new MirrorRequest("main-id"), mirrors, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(("scratch-id", "main-id"), mirrors.Called);
        Assert.True(before.HasChanged);
    }

    [Fact]
    public async Task ClearingAPointer_IsAllowedFromTheMirroringAddress_SoThereIsAlwaysAWayBack()
    {
        var mirroring = new CurrentTenant("main-id", "Main", "main.example", "scratch.example", TenantMatch.Mirror, "scratch-id", "scratch.example");
        var mirrors = new Mirrors((_, _) => true);

        var result = await On(mirroring).SetMirror("scratch-id", new MirrorRequest(null), mirrors, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(("scratch-id", null), mirrors.Called);
    }

    [Fact]
    public async Task SettingAPointerOnTheTenantTheRequestIsOn_IsRefused_BecauseThePersonWouldBeSignedOutWithNoWayBack()
    {
        var mirrors = new Mirrors((_, _) => true);

        var result = await On(Own("scratch-id")).SetMirror("scratch-id", new MirrorRequest("main-id"), mirrors, CancellationToken.None);

        Assert.Contains("locked out", Assert.IsType<ConflictObjectResult>(result).Value as string, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mirrors.Called);
    }

    [Fact]
    public async Task APersonActingInATenantFromAnotherSite_MaySetItsPointer()
    {
        var acting = new CurrentTenant("scratch-id", "Scratch", "scratch.example", "main.example", TenantMatch.Acting);
        var mirrors = new Mirrors((_, _) => true);

        Assert.IsType<NoContentResult>(await On(acting).SetMirror("scratch-id", new MirrorRequest("main-id"), mirrors, CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownTenant_IsNotFound_AndARefusedTargetIsAConflict()
    {
        Assert.IsType<NotFoundResult>(await On(OnAnotherSite).SetMirror("nope", new MirrorRequest("main-id"), new Mirrors((_, _) => false), CancellationToken.None));

        var refusing = new RefusingMirrors();
        var result = await On(OnAnotherSite).SetMirror("scratch-id", new MirrorRequest("third-id"), refusing, CancellationToken.None);
        Assert.Equal("There is no such site to mirror.", Assert.IsType<ConflictObjectResult>(result).Value);
    }

    private sealed class RefusingMirrors : ITenantMirrors
    {
        public Task<bool> SetAsync(string tenantId, string? targetId, CancellationToken cancellationToken = default) => throw new BusinessRuleException("There is no such site to mirror.");
    }

    [Fact]
    public void TheCurrentTenant_TellsTheOwnTenant_WhileMirroring()
    {
        var mirroring = new CurrentTenant("main-id", "Main", "main.example", "scratch.example", TenantMatch.Mirror, "scratch-id", "scratch.example");

        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(On(mirroring).Current()).Value);

        Assert.Contains("scratch-id", json);
        Assert.Contains("Mirror", json);
    }
}
