using System.Composition;
using Adventures.MEF;
using Adventures.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.MEF.SampleWebPlugin;

/// <summary>
/// What a developer's plug-in looks like when it brings an API, a tool, a setting and an event handler: one exported module that says what it is and
/// registers each part under its own plug-in key. Drop the built DLL (with its hash on the approved list) into the host's plug-in directory.
/// </summary>
[Export(typeof(IPluginModule))]
public sealed class SampleWebPluginModule : IPluginModule
{
    public const string Key = "sample-web";

    public PluginDefinition Definition => new(
        Key,
        "Sample web plug-in",
        "Counts the exceptions it hears about, says hello at /api/plugins/sample-web/hello, and offers that as a tool. Proves the plug-in seam from outside the app.",
        "0.1.0",
        900,
        [new PluginSettingDefinition("Greeting", "Greeting", "What the hello endpoint says.", PluginSettingKind.Text, "hello from the sample web plug-in", 80)]);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SampleExceptionCounter>();
        services.AddWidget<SampleNoteWidget>(Key, new WidgetDefinition(
            "sample-web.note", "Sample note", "Shows a line of text in a zone.",
            [new PluginSettingDefinition("Text", "Text", "The line to show.", PluginSettingKind.Text, "a note from the sample plug-in", 120)]));
        services.AddPluginEventHandler<ExceptionRecordedEvent, SampleExceptionHandler>(Key);
        services.AddSingleton<IAppTool>(new AppTool(
            "sample_hello",
            "Says hello from the sample web plug-in and how many exceptions it has heard about.",
            """{ "type": "object", "properties": {} }""",
            "content.draft",
            "GET",
            "api/plugins/sample-web/hello",
            [Key]));
    }
}

/// <summary>How many exceptions the plug-in has heard about since the site started.</summary>
public sealed class SampleExceptionCounter
{
    private int _seen;

    public int Seen => Volatile.Read(ref _seen);

    public void Heard() => Interlocked.Increment(ref _seen);
}

/// <summary>A notification handler: runs off the request, only while the plug-in is on for the tenant.</summary>
public sealed class SampleExceptionHandler(SampleExceptionCounter counter) : IPluginEventHandler<ExceptionRecordedEvent>
{
    public Task HandleAsync(ExceptionRecordedEvent pluginEvent, CancellationToken cancellationToken)
    {
        counter.Heard();
        return Task.CompletedTask;
    }
}

/// <summary>The plug-in's own API: under api/plugins/{key}/, for signed-in callers only, switched off with the plug-in.</summary>
[ApiController]
[Route("api/plugins/sample-web")]
[Authorize]
public sealed class SampleWebController(IPluginSettings settings, SampleExceptionCounter counter) : ControllerBase
{
    [HttpGet("hello")]
    public async Task<IActionResult> Hello(CancellationToken cancellationToken) =>
        Ok(new { greeting = (await settings.GetAsync(SampleWebPluginModule.Key, cancellationToken))["Greeting"], exceptionsHeard = counter.Seen });
}

/// <summary>A widget: returns a line of text for whichever zone an owner puts it in. Its key starts with the plug-in key, which the host requires.</summary>
public sealed class SampleNoteWidget : IWidget
{
    public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
        Task.FromResult($"<p class=\"sample-note\">{System.Net.WebUtility.HtmlEncode(context.Settings["Text"])}</p>");
}
