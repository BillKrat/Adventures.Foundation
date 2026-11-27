using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// A visitor who leaves while the server is still working is not a fault. A database driver reports the cancelled work as its own error (seen live:
/// "Exception while performing SSL handshake" with the cancellation inside it), which filled the exception log. Only that case is set aside: the same
/// error on a request that is still waiting, or any other error, is recorded as before.
/// </summary>
public sealed class AbandonedRequestTests
{
    /// <summary>The shape a driver gives cancelled work: its own exception, the cancellation inside.</summary>
    private static Exception DriverError(Exception? inner = null) =>
        new InvalidOperationException("Exception while performing SSL handshake", inner ?? new OperationCanceledException());

    private static async Task<(DefaultHttpContext Context, Exception? Thrown)> RunAsync(Exception error, bool abandoned)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseAbandonedRequests();
        app.Run(_ => throw error);
        var context = new DefaultHttpContext { RequestServices = services };
        if (abandoned)
        {
            context.RequestAborted = new CancellationToken(canceled: true);
        }

        var thrown = await Record.ExceptionAsync(() => app.Build()(context));
        return (context, thrown);
    }

    [Fact]
    public async Task ADriverErrorFromCancelledWork_OnARequestTheVisitorLeft_IsAnswered499AndGoesNoFurther()
    {
        var (context, thrown) = await RunAsync(DriverError(), abandoned: true);

        Assert.Null(thrown);
        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task ACancellation_OnARequestTheVisitorLeft_IsAnswered499()
    {
        var (context, thrown) = await RunAsync(new TaskCanceledException(), abandoned: true);

        Assert.Null(thrown);
        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task ACancellationDeeperInside_IsFoundToo()
    {
        var (_, thrown) = await RunAsync(DriverError(new IOException("broken", new TaskCanceledException())), abandoned: true);

        Assert.Null(thrown);
    }

    [Fact]
    public async Task TheSameErrorWhileTheVisitorIsStillWaiting_IsARealError()
    {
        var error = DriverError();

        var (_, thrown) = await RunAsync(error, abandoned: false);

        Assert.Same(error, thrown);
    }

    [Fact]
    public async Task AnotherErrorOnARequestTheVisitorLeft_IsStillARealError()
    {
        var error = new InvalidOperationException("a bug", new ArgumentException("bad"));

        var (_, thrown) = await RunAsync(error, abandoned: true);

        Assert.Same(error, thrown);
    }

    [Fact]
    public async Task InARunningHost_NothingIsRecordedForAVisitorWhoLeft_AndARealErrorStillIs()
    {
        var log = new UnhandledExceptionHandlerTests.RecordingExceptionLog("ref-1");
        var left = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IExceptionLogBll>(log);
        await using var app = builder.Build();
        app.UseShieldedErrors();
        app.Run(async context =>
        {
            if (context.Request.Path == "/fault")
            {
                throw DriverError();
            }

            started.SetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted);
            }
            catch (OperationCanceledException cancelled)
            {
                left.SetResult();
                throw DriverError(cancelled);
            }
        });
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var leave = new CancellationTokenSource();
        var abandoned = client.GetAsync("/slow", leave.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        leave.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        await left.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var fault = await client.GetAsync("/fault");
        await app.StopAsync();

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, fault.StatusCode);
        Assert.Equal("GET /fault", Assert.Single(log.Recorded).Request);
    }
}
