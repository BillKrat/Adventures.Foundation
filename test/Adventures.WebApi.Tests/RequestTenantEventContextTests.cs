using Adventures.Entities;
using Adventures.MEF;
using Adventures.WebApi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>A plug-in event is delivered later, in a fresh scope: the tenant of the request that raised it is captured and restored there, so tenant-scoped services answer for the right tenant.</summary>
public sealed class RequestTenantEventContextTests
{
    private static IServiceProvider Request(CurrentTenant tenant)
    {
        var services = new ServiceCollection();
        services.AddScoped<RequestTenant>();
        services.AddScoped<IRequestTenant>(p => p.GetRequiredService<RequestTenant>());
        var provider = services.BuildServiceProvider().CreateScope().ServiceProvider;
        provider.GetRequiredService<RequestTenant>().Set(tenant);
        return provider;
    }

    [Fact]
    public void TheTenantOfTheRequest_IsCapturedByName()
    {
        var tenant = new CurrentTenant("t1", "Adventures Edge", "adventuresontheedge.net", "adventuresontheedge.net", TenantMatch.Domain);

        var snapshot = new RequestTenantEventContext().Capture(Request(tenant));

        Assert.Equal("adventuresontheedge.net", snapshot.Tenant);
        Assert.Equal(tenant, snapshot.State);
    }

    [Fact]
    public void ARequestWithNoTenant_CapturesNone()
    {
        var snapshot = new RequestTenantEventContext().Capture(Request(new CurrentTenant(null, null, null, string.Empty, TenantMatch.None)));

        Assert.Null(snapshot.Tenant);
    }

    [Fact]
    public void RestoringInANewScope_GivesItTheSameTenant()
    {
        var tenant = new CurrentTenant("t1", "Adventures Edge", "adventuresontheedge.net", "adventuresontheedge.net", TenantMatch.Domain);
        var context = new RequestTenantEventContext();
        var snapshot = context.Capture(Request(tenant));
        var delivery = Request(new CurrentTenant(null, null, null, string.Empty, TenantMatch.None));

        context.Restore(delivery, snapshot);

        Assert.Equal(tenant, delivery.GetRequiredService<IRequestTenant>().Current);
    }
}
