using Adventures.Entities;
using Adventures.Ioc.Extensions;
using Adventures.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Client.Tests;

public class CompositionTests
{
    [Fact]
    public void AddAdventuresClient_builds_the_session_the_principal_and_a_remote_presenter()
    {
        var services = new ServiceCollection()
            .AddAdventuresClient(new Uri("https://api.test/"))
            .AddRemotePresenter<DynamicEntity>("api/users");
        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.NotNull(provider.GetRequiredService<ApiSession>());
        Assert.Same(provider.GetRequiredService<AmbientPrincipal>(), provider.GetRequiredService<ICurrentPrincipal>());
        Assert.IsType<HttpEntityPresenter<DynamicEntity>>(provider.GetRequiredService<IEntityPresenter<DynamicEntity>>());
    }

    /// <summary>A host that also runs the reflection scan with this assembly loaded must still build: the scan sees the open-generic presenter.</summary>
    [Fact]
    public void The_lifetime_scan_tolerates_the_client_assembly()
    {
        _ = typeof(HttpEntityPresenter<>).Assembly;
        var services = new ServiceCollection();

        var error = Record.Exception(() => services.AddLifetimeServices());

        Assert.Null(error);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
    }
}
