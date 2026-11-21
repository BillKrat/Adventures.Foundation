using System.Reflection;
using Adventures.Collaboration.WebApi;
using Adventures.MEF;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Collaboration.WebApi.Tests;

/// <summary>The Plugins page shows what discovery loaded and what it refused, to administrators only.</summary>
public sealed class PluginLoadReportTests
{
    private sealed class NoSettings : IPluginSettings
    {
        public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) => [];

        public Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? clear = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<PluginSettingsView> GetForDisplayAsync(string pluginKey, CancellationToken cancellationToken = default) => new(await GetAsync(pluginKey, cancellationToken), []);
    }

    internal sealed class NoPlugins : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void TheReport_IsAnswered_WithWhatLoadedAndWhatFailed()
    {
        var report = new PluginLoadReport([new LoadedPlugin("sample", "Sample", "0.1.0", "directory:Sample.dll")], [new PluginLoadFailure("directory:Bad.dll", "not on the approved list")]);

        var result = new PluginsController(new NoPlugins(), [report], new NoSettings()).LoadReport();

        var answered = Assert.IsType<PluginLoadReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("sample", Assert.Single(answered.Loaded).Key);
        Assert.Equal("directory:Bad.dll", Assert.Single(answered.Failures).Source);
    }

    [Fact]
    public void WithNoDiscoveryRegistered_TheReportIsEmpty_NotAnError()
    {
        var result = new PluginsController(new NoPlugins(), [], new NoSettings()).LoadReport();

        var answered = Assert.IsType<PluginLoadReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(answered.Loaded);
        Assert.Empty(answered.Failures);
    }

    [Fact]
    public void TheReport_IsBehindThePluginsRight_AndNotAnonymous()
    {
        var type = typeof(PluginsController);
        var action = type.GetMethod("LoadReport", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(action);
        Assert.Contains(action!.GetCustomAttributes<HttpGetAttribute>(), a => a.Template == "load-report");
        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(type.GetCustomAttributes<RequireRightAttribute>(), a => a.Right == Rights.PluginsManage);
    }
}
