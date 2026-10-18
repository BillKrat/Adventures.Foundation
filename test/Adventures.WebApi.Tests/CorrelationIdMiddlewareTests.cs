using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Adventures.WebApi.Tests;

public class CorrelationIdMiddlewareTests
{
    private static async Task<(DefaultHttpContext Context, List<IReadOnlyDictionary<string, object>> Scopes, string? SeenInside)> RunAsync(
        string? incomingHeader)
    {
        var context = new DefaultHttpContext();
        var responseFeature = new CallbackResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        if (incomingHeader is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incomingHeader;
        }

        var logger = new CapturingLogger();
        string? seenInside = null;
        var middleware = new CorrelationIdMiddleware(
            ctx =>
            {
                seenInside = CorrelationIdMiddleware.Get(ctx);
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);
        await responseFeature.FireAsync();
        return (context, logger.Scopes, seenInside);
    }

    [Fact]
    public async Task Uses_a_well_formed_incoming_header_and_echoes_it()
    {
        var (context, scopes, seen) = await RunAsync("abc-123_X");

        Assert.Equal("abc-123_X", seen);
        Assert.Equal("abc-123_X", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        Assert.Equal("abc-123_X", scopes.Single()[CorrelationIdMiddleware.LogPropertyName]);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("new\nline")]
    public async Task Replaces_a_malformed_incoming_header(string bad)
    {
        var (context, _, seen) = await RunAsync(bad);

        Assert.NotEqual(bad, seen);
        Assert.True(CorrelationIdMiddleware.IsWellFormed(seen));
        Assert.Equal(seen, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task Rejects_an_over_long_header()
    {
        var (_, _, seen) = await RunAsync(new string('a', 65));

        Assert.NotEqual(new string('a', 65), seen);
    }

    [Fact]
    public async Task Without_a_header_uses_the_current_trace_id()
    {
        using var activity = new Activity("test").Start();

        var (_, _, seen) = await RunAsync(null);

        Assert.Equal(activity.TraceId.ToString(), seen);
        Assert.Equal(seen, activity.GetTagItem(CorrelationIdMiddleware.ActivityTagName));
    }

    [Fact]
    public async Task Without_header_or_trace_generates_an_id()
    {
        Activity.Current = null;

        var (_, _, seen) = await RunAsync(null);

        Assert.True(CorrelationIdMiddleware.IsWellFormed(seen));
        Assert.Equal(32, seen!.Length);
    }

    [Fact]
    public async Task Handler_copies_the_id_to_outbound_requests()
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.ItemsKey] = "req-42";
        var handler = new CorrelationIdHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = new StubHandler(),
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal("req-42", request.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Handler_does_nothing_outside_a_request()
    {
        var handler = new CorrelationIdHandler(new HttpContextAccessor()) { InnerHandler = new StubHandler() };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        await invoker.SendAsync(request, CancellationToken.None);

        Assert.False(request.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    /// <summary>DefaultHttpContext never fires OnStarting callbacks; this one does when the response starts.</summary>
    private sealed class CallbackResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        public async Task FireAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage());
    }

    private sealed class CapturingLogger : ILogger<CorrelationIdMiddleware>
    {
        public List<IReadOnlyDictionary<string, object>> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IReadOnlyDictionary<string, object> dictionary)
            {
                Scopes.Add(dictionary);
            }

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
