using Adventures.MEF;
using Adventures.MEF.SampleWebPlugin;
using Adventures.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.MEF.WebApi.Tests;

/// <summary>
/// A plug-in may bring its own API controllers and tools into the host, under rules the host enforces before anything is registered: every route lives
/// under api/plugins/{its key}/, nothing is anonymous, every tool is tied to the plug-in (so it disappears with it) and calls a route of the plug-in.
/// A plug-in that breaks a rule does not load, and the reason is reported.
/// </summary>
public sealed class PluginWebRulesTests
{
    // ---- controllers that break each rule (reflected on, never registered)

    [ApiController, Route("api/plugins/bad/open"), AllowAnonymous]
    public sealed class AnonymousByClassController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [ApiController, Route("api/plugins/bad/open2")]
    public sealed class AnonymousByActionController : ControllerBase
    {
        [HttpGet, AllowAnonymous]
        public IActionResult Get() => Ok();
    }

    [ApiController, Route("api/admin/users")]
    public sealed class OutsideThePrefixController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [ApiController, Route("api/plugins/other/x")]
    public sealed class AnotherPluginsPrefixController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [ApiController]
    public sealed class NoRouteController : ControllerBase
    {
        [HttpGet("anything")]
        public IActionResult Get() => Ok();
    }

    [ApiController, Route("api/plugins/bad")]
    public sealed class ActionEscapesController : ControllerBase
    {
        [HttpGet("/api/auth/login")]
        public IActionResult Get() => Ok();
    }

    [ApiController, Route("API/Plugins/Bad/Fine")]
    [Authorize]
    public sealed class CaseInsensitiveController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [Theory]
    [InlineData(typeof(AnonymousByClassController), "anonymous")]
    [InlineData(typeof(AnonymousByActionController), "anonymous")]
    [InlineData(typeof(OutsideThePrefixController), "api/plugins/bad/")]
    [InlineData(typeof(AnotherPluginsPrefixController), "api/plugins/bad/")]
    [InlineData(typeof(NoRouteController), "route")]
    [InlineData(typeof(ActionEscapesController), "api/plugins/bad/")]
    public void AControllerThatBreaksARule_IsRefused_WithTheReason(Type controller, string mentions)
    {
        var problems = PluginWebRules.CheckController(controller, "bad");

        Assert.Contains(problems, p => p.Contains(mentions, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ACorrectController_PassesTheRules_EvenWithOtherCapitals()
    {
        Assert.Empty(PluginWebRules.CheckController(typeof(CaseInsensitiveController), "bad"));
    }

    // ---- tools

    private static IServiceCollection WithTool(IAppTool tool) => new ServiceCollection().AddSingleton(tool);

    [Fact]
    public void AToolTiedToThePlugin_UnderItsPrefix_Passes()
    {
        var tool = new AppTool("t", "d", "{}", "content.draft", "GET", "api/plugins/bad/hello", ["bad"]);

        Assert.Empty(PluginWebRules.CheckTools(WithTool(tool), "bad"));
    }

    [Theory]
    [InlineData("api/auth/login")]
    [InlineData("api/plugins/other/hello")]
    [InlineData("api/plugins/bad")]
    public void AToolThatCallsARouteOutsideThePlugin_IsRefused(string path)
    {
        var tool = new AppTool("t", "d", "{}", "content.draft", "GET", path, ["bad"]);

        Assert.NotEmpty(PluginWebRules.CheckTools(WithTool(tool), "bad"));
    }

    [Fact]
    public void AToolNotTiedToThePlugin_WouldOutliveItsSwitch_AndIsRefused()
    {
        var always = new AppTool("t", "d", "{}", "content.draft", "GET", "api/plugins/bad/hello");
        var other = new AppTool("t", "d", "{}", "content.draft", "GET", "api/plugins/bad/hello", ["other"]);

        Assert.Contains(PluginWebRules.CheckTools(WithTool(always), "bad"), p => p.Contains("switch", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(PluginWebRules.CheckTools(WithTool(other), "bad"));
    }

    // ---- the whole check, on the real sample plug-in's assembly

    [Fact]
    public void TheSampleWebPlugin_PassesEveryRule()
    {
        var services = new ServiceCollection();
        new SampleWebPluginModule().ConfigureServices(services);

        var problem = PluginWebRules.Validate(SampleWebPluginModule.Key, typeof(SampleWebPluginModule).Assembly, services);

        Assert.Null(problem);
    }

    [Fact]
    public void ThisTestAssemblyWithItsBadControllers_FailsTheCheck_AsAPluginWould()
    {
        var problem = PluginWebRules.Validate("bad", typeof(PluginWebRulesTests).Assembly, new ServiceCollection());

        Assert.NotNull(problem);
        Assert.Contains("AnonymousByClassController", problem);
    }

    // ---- the switch: a plug-in that is off for the tenant has no endpoints

    private sealed class FakePlugins(bool on) : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(on);

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(on ? new HashSet<string> { "bad" } : new HashSet<string>());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(bool Reached, IActionResult? Result)> RunFilterAsync(bool pluginOn)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IPlugins>(new FakePlugins(pluginOn)).BuildServiceProvider() };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new Dictionary<string, object?>(), new object());
        var reached = false;
        await new PluginEnabledFilter("bad").OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });
        return (reached, context.Result);
    }

    [Fact]
    public async Task WhileThePluginIsOn_TheActionRuns()
    {
        var (reached, result) = await RunFilterAsync(pluginOn: true);

        Assert.True(reached);
        Assert.Null(result);
    }

    [Fact]
    public async Task WhileThePluginIsOff_TheEndpointIsNotFound()
    {
        var (reached, result) = await RunFilterAsync(pluginOn: false);

        Assert.False(reached);
        Assert.IsType<NotFoundResult>(result);
    }

    // ---- widgets

    private sealed class NoWidget : IWidget
    {
        public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) => Task.FromResult("<p>x</p>");
    }

    [Fact]
    public void AWidgetWhoseKeyStartsWithThePluginsKey_AndIsOwnedByIt_Passes()
    {
        var services = new ServiceCollection().AddWidget<NoWidget>("bad", new WidgetDefinition("bad.note", "Note", "d"));

        Assert.Empty(PluginWebRules.CheckWidgets(services, "bad"));
    }

    [Theory]
    [InlineData("text-box")]
    [InlineData("other.note")]
    [InlineData("bad")]
    public void AWidgetKeyThatCouldShadowAnotherWidget_IsRefused(string key)
    {
        var services = new ServiceCollection().AddWidget<NoWidget>("bad", new WidgetDefinition(key, "Note", "d"));

        Assert.NotEmpty(PluginWebRules.CheckWidgets(services, "bad"));
    }

    [Fact]
    public void AWidgetRegisteredForAnotherPlugin_IsRefused()
    {
        var services = new ServiceCollection().AddWidget<NoWidget>("other", new WidgetDefinition("bad.note", "Note", "d"));

        Assert.Contains(PluginWebRules.CheckWidgets(services, "bad"), p => p.Contains("other", StringComparison.Ordinal));
    }
}
