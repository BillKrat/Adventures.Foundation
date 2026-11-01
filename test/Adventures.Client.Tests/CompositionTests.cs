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

public class ApiAddressTests
{
    [Theory]
    [InlineData("https://api.example.com/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("https://localhost:7001/")]
    public void Accepts_https_and_loopback(string address)
    {
        Assert.NotNull(new ServiceCollection().AddAdventuresClient(new Uri(address)));
    }

    [Theory]
    [InlineData("http://api.example.com/")]
    [InlineData("http://192.168.1.10:5000/")]
    [InlineData("ftp://api.example.com/")]
    public void Refuses_a_plain_http_or_unknown_scheme_to_a_remote_host(string address)
    {
        var error = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAdventuresClient(new Uri(address)));
        Assert.Contains("https", error.Message);
    }
}
