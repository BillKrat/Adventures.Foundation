using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.Identity.WebApi;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>Creating a tenant is the platform administrator's, answers the new tenant, refuses with the reason (409) and clears the caches so the new domain works at once.</summary>
public sealed class TenantCreateEndpointTests
{
    private sealed class Creator(bool refuse = false) : ITenantCreator
    {
        public (string Name, string Domain)? Asked { get; private set; }

        public Task<TenantHostInfo> CreateAsync(string name, string domain, CancellationToken cancellationToken = default)
        {
            Asked = (name, domain);
            return refuse ? throw new BusinessRuleException("photos.example.org already reaches DEV Main.") : Task.FromResult(new TenantHostInfo("new-id", name, domain, []));
        }
    }

    private static TenantController Controller(TenantCacheInvalidator? invalidator = null) =>
        new(new FixedRequestTenant(new CurrentTenant(null, null, null, "h", TenantMatch.None)), invalidator);

    [Fact]
    public void CreatingATenant_NeedsThePlatformRight()
    {
        var guard = Assert.Single(typeof(TenantController).GetMethod(nameof(TenantController.CreateTenant))!.GetCustomAttributes(typeof(RequireRightAttribute), inherit: true).Cast<RequireRightAttribute>());

        Assert.Equal(Rights.PlatformManage, guard.Right);
    }

    [Fact]
    public async Task ANewTenantIsAnswered_AndTheCachesAreCleared()
    {
        var invalidator = new TenantCacheInvalidator();
        var before = invalidator.Token;
        var creator = new Creator();

        var result = await Controller(invalidator).CreateTenant(new NewTenantRequest("Photos", "photos.example.org"), creator, CancellationToken.None);

        var created = Assert.IsType<TenantHostInfo>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(("Photos", "photos.example.org"), (created.Name, created.Domain));
        Assert.Equal(("Photos", "photos.example.org"), creator.Asked);
        Assert.True(before.HasChanged);
    }

    [Fact]
    public async Task ARefusalIsA409WithTheReason_AndNothingIsCleared()
    {
        var invalidator = new TenantCacheInvalidator();
        var before = invalidator.Token;

        var result = await Controller(invalidator).CreateTenant(new NewTenantRequest("Photos", "photos.example.org"), new Creator(refuse: true), CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.False(before.HasChanged);
    }
}
