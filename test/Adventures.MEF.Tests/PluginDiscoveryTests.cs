using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.MEF.Tests;

/// <summary>
/// Plug-in discovery: a plug-in is an exported <see cref="IPluginModule"/> found in the host's own assemblies and in a plug-in directory of
/// DLLs. A DLL is code, so the directory loads nothing that is not on its approved list by SHA-256, one bad file never stops the others, and
/// every refusal is reported as a failure (never silently skipped).
/// </summary>
public sealed class PluginDiscoveryTests : IDisposable
{
    private const string SampleDll = "Adventures.MEF.SamplePlugin.dll";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "adv-plugins-" + Guid.NewGuid().ToString("N"));

    public PluginDiscoveryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // a loaded DLL can keep its file; the temporary directory is cleaned up by the operating system later
        }
    }

    private string CopySample(string? as_ = null)
    {
        var target = Path.Combine(_directory, as_ ?? SampleDll);
        File.Copy(Path.Combine(AppContext.BaseDirectory, SampleDll), target);
        foreach (var companion in new[] { "Adventures.MEF.SamplePlugin.deps.json" })
        {
            var source = Path.Combine(AppContext.BaseDirectory, companion);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(_directory, companion), overwrite: true);
            }
        }

        return target;
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private void Approve(params (string File, string Hash)[] entries) =>
        File.WriteAllText(Path.Combine(_directory, PluginDirectoryOptions.ApprovedFileName), JsonSerializer.Serialize(entries.ToDictionary(e => e.File, e => e.Hash)));

    private PluginDirectoryOptions Directory_(bool requireApproval = true) => new(_directory, requireApproval);

    [Fact]
    public void AHostAssembly_WithAnExportedModule_IsDiscovered()
    {
        var result = PluginDiscovery.Discover([typeof(PluginDiscoveryTests).Assembly, SampleAssembly()], directory: null);

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("sample", plugin.Definition.Key);
        Assert.Equal("assembly:Adventures.MEF.SamplePlugin", plugin.Source);
        Assert.Empty(result.Failures);
    }

    private static Assembly SampleAssembly() => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, SampleDll));

    [Fact]
    public void AnApprovedDll_InThePluginDirectory_IsLoaded_AndItsServicesReachTheHost()
    {
        var path = CopySample();
        Approve((SampleDll, Sha256(path)));

        var result = PluginDiscovery.Discover([], Directory_());

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("sample", plugin.Definition.Key);
        Assert.Equal("directory:" + SampleDll, plugin.Source);
        Assert.Empty(result.Failures);

        var services = new ServiceCollection();
        plugin.Module.ConfigureServices(services);
        Assert.Contains(services, d => d.ServiceType.Name == "SampleMarker");
    }

    [Fact]
    public void ADll_NotOnTheApprovedList_IsRefused_AndReported()
    {
        CopySample();
        Approve();

        var result = PluginDiscovery.Discover([], Directory_());

        Assert.Empty(result.Plugins);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("directory:" + SampleDll, failure.Source);
        Assert.Contains("not on the approved list", failure.Reason);
    }

    [Fact]
    public void ADll_WhoseHashDiffers_IsRefused_AsTampered()
    {
        CopySample();
        Approve((SampleDll, new string('0', 64)));

        var result = PluginDiscovery.Discover([], Directory_());

        Assert.Empty(result.Plugins);
        Assert.Contains("does not match", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void WithNoApprovedListFile_NothingLoads_WhenApprovalIsRequired()
    {
        CopySample();

        var result = PluginDiscovery.Discover([], Directory_());

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Failures, f => f.Reason.Contains("approved"));
    }

    [Fact]
    public void AMalformedApprovedList_LoadsNothing_AndSaysSo()
    {
        CopySample();
        File.WriteAllText(Path.Combine(_directory, PluginDirectoryOptions.ApprovedFileName), "{ not json");

        var result = PluginDiscovery.Discover([], Directory_());

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Failures, f => f.Source == "directory" && f.Reason.Contains("approved"));
    }

    [Fact]
    public void WhenApprovalIsOff_AnyDll_IsLoaded()
    {
        CopySample();

        var result = PluginDiscovery.Discover([], Directory_(requireApproval: false));

        Assert.Equal("sample", Assert.Single(result.Plugins).Definition.Key);
    }

    [Fact]
    public void ABadFile_IsReported_AndTheGoodOneStillLoads()
    {
        var path = CopySample();
        File.WriteAllText(Path.Combine(_directory, "broken.dll"), "this is not an assembly");
        Approve((SampleDll, Sha256(path)), ("broken.dll", Sha256(Path.Combine(_directory, "broken.dll"))));

        var result = PluginDiscovery.Discover([], Directory_());

        Assert.Equal("sample", Assert.Single(result.Plugins).Definition.Key);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("directory:broken.dll", failure.Source);
    }

    [Fact]
    public void ASecondPlugin_WithTheSameKey_IsRefused_AsADuplicate()
    {
        var path = CopySample();
        Approve((SampleDll, Sha256(path)));

        var result = PluginDiscovery.Discover([SampleAssembly()], Directory_());

        Assert.Single(result.Plugins);
        Assert.Contains("already registered", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void ADirectoryThatDoesNotExist_IsReported_AndAnUnsetOneIsNot()
    {
        var missing = PluginDiscovery.Discover([], new PluginDirectoryOptions(Path.Combine(_directory, "nope")));
        var unset = PluginDiscovery.Discover([], new PluginDirectoryOptions(null));

        Assert.Contains("not found", Assert.Single(missing.Failures).Reason);
        Assert.Empty(unset.Failures);
        Assert.Empty(unset.Plugins);
    }

    [Fact]
    public void OnlyTopLevelDlls_AreConsidered()
    {
        var sub = Path.Combine(_directory, "nested");
        Directory.CreateDirectory(sub);
        File.Copy(Path.Combine(AppContext.BaseDirectory, SampleDll), Path.Combine(sub, SampleDll));
        Approve();

        var result = PluginDiscovery.Discover([], Directory_());

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Failures);
    }
}
