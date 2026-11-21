using System.Reflection;
using System.Text.Json;
using Adventures.Common.Exceptions;
using Adventures.Collaboration.WebApi;
using Adventures.Entities;
using Adventures.MEF;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Collaboration.WebApi.Tests;

/// <summary>The Customize screen's API: a plug-in's declared settings with their values, and saving them; administrators only, one plug-in at a time.</summary>
public sealed class PluginSettingsControllerTests
{
    private static readonly PluginSettingDefinition Greeting = new("Greeting", "Greeting", "Shown.", PluginSettingKind.Text, "hello", 20, Validate: v => v == "no" ? "nope" : null);

    private sealed class FakeSettings : IPluginSettings
    {
        public Dictionary<string, string?>? Saved { get; private set; }

        public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) =>
            pluginKey == "sample" ? [Greeting] : throw new BusinessRuleException($"There is no plug-in '{pluginKey}'.");

        public Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default)
        {
            _ = DefinitionsOf(pluginKey);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["Greeting"] = "hello" });
        }

        public Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
        {
            _ = DefinitionsOf(pluginKey);
            if (values.TryGetValue("Greeting", out var v) && v == "bad")
            {
                throw new BusinessRuleException("Greeting is not accepted.");
            }

            Saved = new Dictionary<string, string?>(values);
            return Task.CompletedTask;
        }
    }

    private static PluginsController Controller(FakeSettings settings) => new(new PluginLoadReportTests.NoPlugins(), [], settings);

    [Fact]
    public async Task TheSettings_AreAnsweredWithTheirDefinitionsAndValues()
    {
        var result = await Controller(new FakeSettings()).GetSettings("sample", CancellationToken.None);

        var model = Assert.IsType<PluginSettingsModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Greeting", Assert.Single(model.Definitions).Name);
        Assert.Equal("hello", model.Values["Greeting"]);
    }

    [Fact]
    public async Task AnUnknownPlugin_IsNotFound_ForReadingAndForSaving()
    {
        var controller = Controller(new FakeSettings());

        Assert.IsType<NotFoundResult>((await controller.GetSettings("nope", CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await controller.UpdateSettings("nope", new PluginSettingsUpdate(new Dictionary<string, string?>()), CancellationToken.None));
    }

    [Fact]
    public async Task Saving_StoresTheValues_AndAnswersNoContent()
    {
        var settings = new FakeSettings();

        var result = await Controller(settings).UpdateSettings("sample", new PluginSettingsUpdate(new Dictionary<string, string?> { ["Greeting"] = "hi" }), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("hi", settings.Saved!["Greeting"]);
    }

    [Fact]
    public async Task ARefusedValue_IsAConflict_WithItsMessage_AndNothingIsSaved()
    {
        var settings = new FakeSettings();

        var result = await Controller(settings).UpdateSettings("sample", new PluginSettingsUpdate(new Dictionary<string, string?> { ["Greeting"] = "bad" }), CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Null(settings.Saved);
    }

    [Fact]
    public void TheDefinitions_SerializeWithoutThePluginsValidator()
    {
        var json = JsonSerializer.Serialize(new PluginSettingsModel([Greeting], new Dictionary<string, string> { ["Greeting"] = "hello" }));

        Assert.Contains("Greeting", json);
        Assert.DoesNotContain("Validate", json);
        Assert.Contains("\"Kind\":\"Text\"", json);
    }

    [Fact]
    public void BothActions_AreBehindThePluginsRight_AndNotAnonymous()
    {
        var type = typeof(PluginsController);
        Assert.Contains(type.GetCustomAttributes<RequireRightAttribute>(), a => a.Right == Rights.PluginsManage);
        foreach (var (name, verb) in new[] { ("GetSettings", typeof(HttpGetAttribute)), ("UpdateSettings", typeof(HttpPutAttribute)) })
        {
            var action = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(action);
            Assert.NotEmpty(action!.GetCustomAttributes(verb));
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
        }
    }
}
