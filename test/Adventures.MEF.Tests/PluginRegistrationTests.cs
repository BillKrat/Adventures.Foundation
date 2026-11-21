using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Adventures.MEF;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.MEF.Tests;

/// <summary>What the host gets from discovery: each plug-in's definition and services registered, a report of what loaded and what did not, and a plug-in that fails to configure adds nothing.</summary>
public sealed class PluginRegistrationTests : IDisposable
{
    private const string SampleDll = "Adventures.MEF.SamplePlugin.dll";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "adv-plugreg-" + Guid.NewGuid().ToString("N"));

    public PluginRegistrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // a loaded DLL can keep its file
        }
    }

    private void PlaceApprovedSample()
    {
        var target = Path.Combine(_directory, SampleDll);
        File.Copy(Path.Combine(AppContext.BaseDirectory, SampleDll), target);
        var deps = Path.Combine(AppContext.BaseDirectory, "Adventures.MEF.SamplePlugin.deps.json");
        if (File.Exists(deps))
        {
            File.Copy(deps, Path.Combine(_directory, "Adventures.MEF.SamplePlugin.deps.json"));
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
        File.WriteAllText(Path.Combine(_directory, PluginDirectoryOptions.ApprovedFileName), JsonSerializer.Serialize(new Dictionary<string, string> { [SampleDll] = hash }));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void ADiscoveredPlugin_HasItsDefinitionAndServicesRegistered_AndIsInTheReport()
    {
        PlaceApprovedSample();
        var services = new ServiceCollection();

        services.AddPluginDiscovery(Config(("Plugins:Directory", _directory)), []);

        using var provider = services.BuildServiceProvider();
        var definition = Assert.Single(provider.GetServices<PluginDefinition>());
        Assert.Equal("sample", definition.Key);
        Assert.Contains(services, d => d.ServiceType.Name == "SampleMarker");
        var report = provider.GetRequiredService<PluginLoadReport>();
        var loaded = Assert.Single(report.Loaded);
        Assert.Equal(("sample", "directory:" + SampleDll), (loaded.Key, loaded.Source));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void WithNoDirectorySetting_NothingIsScanned_AndTheReportIsEmpty()
    {
        var services = new ServiceCollection();

        services.AddPluginDiscovery(Config(), []);

        using var provider = services.BuildServiceProvider();
        var report = provider.GetRequiredService<PluginLoadReport>();
        Assert.Empty(report.Loaded);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void ARefusedDll_IsInTheReport_AndNothingFromItIsRegistered()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, SampleDll), Path.Combine(_directory, SampleDll));
        File.WriteAllText(Path.Combine(_directory, PluginDirectoryOptions.ApprovedFileName), "{}");
        var services = new ServiceCollection();

        services.AddPluginDiscovery(Config(("Plugins:Directory", _directory)), []);

        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<PluginDefinition>());
        Assert.DoesNotContain(services, d => d.ServiceType.Name == "SampleMarker");
        Assert.Contains("not on the approved list", Assert.Single(provider.GetRequiredService<PluginLoadReport>().Failures).Reason);
    }

    [Fact]
    public void ApprovalCanBeSwitchedOff_ByTheSetting()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, SampleDll), Path.Combine(_directory, SampleDll));
        var services = new ServiceCollection();

        services.AddPluginDiscovery(Config(("Plugins:Directory", _directory), ("Plugins:RequireApproval", "false")), []);

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<PluginDefinition>());
    }

    private sealed class FaultyModule : IPluginModule
    {
        public PluginDefinition Definition => new("faulty", "Faulty", "Throws when it configures.", "1.0.0", 1);

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton("half-registered");
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void APluginThatFailsToConfigure_AddsNothing_AndIsReportedAsAFailure()
    {
        var services = new ServiceCollection();
        var result = new PluginDiscoveryResult([new DiscoveredPlugin(new FaultyModule().Definition, new FaultyModule(), "assembly:test")], []);

        var report = PluginRegistration.Register(services, result);

        Assert.Empty(report.Loaded);
        var failure = Assert.Single(report.Failures);
        Assert.Equal("assembly:test", failure.Source);
        Assert.Contains("boom", failure.Reason);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(string));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(PluginDefinition));
    }
}
