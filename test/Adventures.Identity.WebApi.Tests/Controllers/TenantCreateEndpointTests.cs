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

    private sealed class Provisioner(bool refuse = false, bool makesAccounts = true) : ISiteProvisioner
    {
        public (string Domain, string Name)? Asked { get; private set; }

        public bool CreatesAccounts { get; } = makesAccounts;

        public Task<TenantHostInfo> CreateAsync(string domain, string name, CancellationToken cancellationToken = default)
        {
            Asked = (domain, name);
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

        var result = await Controller(invalidator).CreateTenant(new NewTenantRequest("Photos", "photos.example.org"), creator, new Provisioner(makesAccounts: false), CancellationToken.None);

        var created = Assert.IsType<CreatedTenant>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(("Photos", "photos.example.org"), (created.Name, created.Domain));
        Assert.Null(created.SignIn);   // this install has no sign-in store, so no account was made
        Assert.Equal(("Photos", "photos.example.org"), creator.Asked);
        Assert.True(before.HasChanged);
    }

    [Fact]
    public async Task ARefusalIsA409WithTheReason_AndNothingIsCleared()
    {
        var invalidator = new TenantCacheInvalidator();
        var before = invalidator.Token;

        var result = await Controller(invalidator).CreateTenant(new NewTenantRequest("Photos", "photos.example.org"), new Creator(refuse: true), new Provisioner(makesAccounts: false), CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.False(before.HasChanged);
    }

    [Fact]
    public async Task OnAnInstallWithASignInStore_ANewTenantComesWithItsOwnAdmin_ThroughTheSiteProvisioner()
    {
        var creator = new Creator();
        var provisioner = new Provisioner();

        var result = await Controller().CreateTenant(new NewTenantRequest("Photos", "photos.example.org"), creator, provisioner, CancellationToken.None);

        var created = Assert.IsType<CreatedTenant>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(("photos.example.org", "Photos"), provisioner.Asked);
        Assert.Null(creator.Asked);   // the provisioner makes the tenant itself
        Assert.Equal("Admin", created.SignIn!.UserName);
        Assert.True(created.SignIn.MustChangePassword);
    }

    [Fact]
    public async Task AProvisionerRefusal_IsA409WithTheReason()
    {
        var result = await Controller().CreateTenant(new NewTenantRequest("Photos", "photos.example.org"), new Creator(), new Provisioner(refuse: true), CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }
}
