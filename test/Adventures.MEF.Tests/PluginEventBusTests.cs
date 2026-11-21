using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Adventures.MEF.Tests;

/// <summary>
/// The in-process event bus for plug-ins. Notifications are delivered off the request (a slow plug-in never slows an answer), only to plug-ins that
/// are on for the tenant, with every handler isolated (a throwing or hanging one is logged and never stops the rest) and no event raised from
/// inside a handler (so a failing plug-in cannot make an exception that triggers itself). Interceptors are awaited in order and may refuse.
/// </summary>
public sealed class PluginEventBusTests : IAsyncLifetime
{
    private sealed record Ping(string Text) : IPluginEvent;

    private sealed record Other(string Text) : IPluginEvent;

    /// <summary>What the handlers saw, shared by every test host.</summary>
    private sealed class Seen
    {
        public List<string> Handled { get; } = [];

        public List<string> Tenants { get; } = [];

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SemaphoreSlim Released { get; } = new(0);

        public object Lock { get; } = new();

        public void Note(string what)
        {
            lock (Lock)
            {
                Handled.Add(what);
            }

            Released.Release();
        }

        public async Task WaitForAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Assert.True(await Released.WaitAsync(TimeSpan.FromSeconds(5)), "a handler was not called in time");
            }
        }
    }

    private sealed class FirstHandler(Seen seen, PluginEventOrigin origin) : IPluginEventHandler<Ping>
    {
        public Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            lock (seen.Lock)
            {
                seen.Tenants.Add(origin.Tenant ?? "none");
            }

            seen.Note("first:" + pluginEvent.Text);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondHandler(Seen seen) : IPluginEventHandler<Ping>
    {
        public Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            seen.Note("second:" + pluginEvent.Text);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IPluginEventHandler<Ping>
    {
        public Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("the webhook is down");
    }

    private sealed class HangingHandler : IPluginEventHandler<Ping>
    {
        public async Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken) => await Task.Delay(Timeout.Infinite);
    }

    private sealed class BlockedHandler(Seen seen) : IPluginEventHandler<Ping>
    {
        public async Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            await seen.Gate.Task;
            seen.Note("blocked:" + pluginEvent.Text);
        }
    }

    private sealed class RepublishingHandler(Seen seen, IPluginEvents events) : IPluginEventHandler<Ping>
    {
        public async Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            seen.Note("republisher:" + pluginEvent.Text);
            await events.PublishAsync(new Ping("from inside a handler"), cancellationToken);
        }
    }

    private sealed class FakePlugins(params string[] enabled) : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(enabled.Contains(key));

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(enabled.ToHashSet());

        public Task SetEnabledAsync(string key, bool enabledNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeContext : IPluginEventContext
    {
        public int Captures { get; private set; }

        public PluginEventSnapshot Capture(IServiceProvider requestServices)
        {
            Captures++;
            return new PluginEventSnapshot("tenant-a", "state-a");
        }

        public void Restore(IServiceProvider deliveryServices, PluginEventSnapshot snapshot) => deliveryServices.GetRequiredService<RestoredState>().Value = (string?)snapshot.State;
    }

    private sealed class RestoredState
    {
        public string? Value { get; set; }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public static readonly List<string> Warnings = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                lock (Warnings)
                {
                    Warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    private readonly Seen _seen = new();
    private readonly FakeContext _context = new();
    private ServiceProvider _provider = null!;
    private IHostedService _worker = null!;

    public Task InitializeAsync()
    {
        CapturingLogger<PluginEventBus>.Warnings.Clear();
        CapturingLogger<PluginEventDispatcher>.Warnings.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
        }

        _provider?.Dispose();
    }

    private async Task StartAsync(Action<IServiceCollection> register, params string[] enabledPlugins)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddSingleton(_seen);
        services.AddSingleton<IPluginEventContext>(_context);
        services.AddScoped<RestoredState>();
        services.AddScoped<IPlugins>(_ => new FakePlugins(enabledPlugins));
        services.AddPluginEvents(options =>
        {
            options.HandlerTimeout = TimeSpan.FromMilliseconds(300);
            options.InterceptorTimeout = TimeSpan.FromMilliseconds(300);
            options.QueueCapacity = 3;
        });
        register(services);
        _provider = services.BuildServiceProvider();
        _worker = _provider.GetServices<IHostedService>().Single(s => s is PluginEventDispatcher);
        await _worker.StartAsync(CancellationToken.None);
    }

    private IPluginEvents Bus() => _provider.CreateScope().ServiceProvider.GetRequiredService<IPluginEvents>();

    [Fact]
    public async Task AnEnabledPluginsHandler_ReceivesTheEvent_WithTheTenantItWasRaisedFor()
    {
        await StartAsync(s => s.AddPluginEventHandler<Ping, FirstHandler>("a"), "a");

        await Bus().PublishAsync(new Ping("hi"));
        await _seen.WaitForAsync(1);

        Assert.Equal(["first:hi"], _seen.Handled);
        Assert.Equal(["tenant-a"], _seen.Tenants);
    }

    [Fact]
    public async Task APluginThatIsOff_ForThisTenant_GetsNothing()
    {
        await StartAsync(s =>
        {
            s.AddPluginEventHandler<Ping, FirstHandler>("off");
            s.AddPluginEventHandler<Ping, SecondHandler>("on");
        }, "on");

        await Bus().PublishAsync(new Ping("x"));
        await _seen.WaitForAsync(1);
        await Task.Delay(100);

        Assert.Equal(["second:x"], _seen.Handled);
    }

    [Fact]
    public async Task APublish_DoesNotWaitForASlowHandler()
    {
        await StartAsync(s => s.AddPluginEventHandler<Ping, BlockedHandler>("a"), "a");

        var publishing = Bus().PublishAsync(new Ping("slow"));
        var finished = await Task.WhenAny(publishing, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(publishing, finished);
        _seen.Gate.SetResult();
        await _seen.WaitForAsync(1);
    }

    [Fact]
    public async Task AThrowingHandler_IsLogged_AndTheNextOneStillRuns()
    {
        await StartAsync(s =>
        {
            s.AddPluginEventHandler<Ping, ThrowingHandler>("a");
            s.AddPluginEventHandler<Ping, SecondHandler>("a");
        }, "a");

        await Bus().PublishAsync(new Ping("y"));
        await _seen.WaitForAsync(1);

        Assert.Equal(["second:y"], _seen.Handled);
        Assert.Contains(CapturingLogger<PluginEventDispatcher>.Warnings, w => w.Contains("ThrowingHandler") && w.Contains("the webhook is down"));
    }

    [Fact]
    public async Task AHangingHandler_IsGivenUp_AfterItsTimeout_AndTheNextOneStillRuns()
    {
        await StartAsync(s =>
        {
            s.AddPluginEventHandler<Ping, HangingHandler>("a");
            s.AddPluginEventHandler<Ping, SecondHandler>("a");
        }, "a");

        await Bus().PublishAsync(new Ping("z"));
        await _seen.WaitForAsync(1);

        Assert.Equal(["second:z"], _seen.Handled);
        Assert.Contains(CapturingLogger<PluginEventDispatcher>.Warnings, w => w.Contains("HangingHandler") && w.Contains("timed out"));
    }

    [Fact]
    public async Task AnEventRaisedFromInsideAHandler_IsIgnored_SoAFailingPluginCannotTriggerItself()
    {
        await StartAsync(s => s.AddPluginEventHandler<Ping, RepublishingHandler>("a"), "a");

        await Bus().PublishAsync(new Ping("outer"));
        await _seen.WaitForAsync(1);
        await Task.Delay(200);

        Assert.Equal(["republisher:outer"], _seen.Handled);
    }

    [Fact]
    public async Task HandlersAreCalledInTheOrderTheyWereRegistered()
    {
        await StartAsync(s =>
        {
            s.AddPluginEventHandler<Ping, FirstHandler>("a");
            s.AddPluginEventHandler<Ping, SecondHandler>("a");
        }, "a");

        await Bus().PublishAsync(new Ping("o"));
        await _seen.WaitForAsync(2);

        Assert.Equal(["first:o", "second:o"], _seen.Handled);
    }

    [Fact]
    public async Task AnEventNobodyHandles_CostsNothing_NoContextIsCaptured()
    {
        await StartAsync(s => s.AddPluginEventHandler<Ping, FirstHandler>("a"), "a");

        await Bus().PublishAsync(new Other("nobody listens"));

        Assert.Equal(0, _context.Captures);
    }

    [Fact]
    public async Task AFullQueue_NeverBlocksOrThrows_AndTheDropIsLogged()
    {
        await StartAsync(s => s.AddPluginEventHandler<Ping, BlockedHandler>("a"), "a");
        var bus = Bus();

        var publishing = Task.Run(async () =>
        {
            for (var i = 0; i < 12; i++)
            {
                await bus.PublishAsync(new Ping("flood" + i));
            }
        });
        var finished = await Task.WhenAny(publishing, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.Same(publishing, finished);
        await publishing;
        Assert.Contains(CapturingLogger<PluginEventBus>.Warnings, w => w.Contains("dropped"));
        _seen.Gate.SetResult();
    }

    private sealed class AllowingInterceptor(Seen seen) : IPluginEventInterceptor<Ping>
    {
        public Task<InterceptResult> InterceptAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            lock (seen.Lock)
            {
                seen.Handled.Add("allow");
            }

            return Task.FromResult(InterceptResult.Allow);
        }
    }

    private sealed class RefusingInterceptor : IPluginEventInterceptor<Ping>
    {
        public Task<InterceptResult> InterceptAsync(Ping pluginEvent, CancellationToken cancellationToken) => Task.FromResult(InterceptResult.Cancel("not on a Friday"));
    }

    private sealed class BrokenInterceptor : IPluginEventInterceptor<Ping>
    {
        public Task<InterceptResult> InterceptAsync(Ping pluginEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("interceptor bug");
    }

    private sealed class SlowInterceptor : IPluginEventInterceptor<Ping>
    {
        public async Task<InterceptResult> InterceptAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite);
            return InterceptResult.Allow;
        }
    }

    [Fact]
    public async Task WithNoInterceptor_AnythingIsAllowed()
    {
        await StartAsync(_ => { }, "a");

        Assert.False((await Bus().InterceptAsync(new Ping("p"))).Cancelled);
    }

    [Fact]
    public async Task AnInterceptorThatRefuses_StopsTheChain_WithItsReason()
    {
        await StartAsync(s =>
        {
            s.AddPluginEventInterceptor<Ping, AllowingInterceptor>("a");
            s.AddPluginEventInterceptor<Ping, RefusingInterceptor>("a");
            s.AddPluginEventInterceptor<Ping, AllowingInterceptor>("b");
        }, "a", "b");

        var result = await Bus().InterceptAsync(new Ping("p"));

        Assert.True(result.Cancelled);
        Assert.Equal("not on a Friday", result.Reason);
        Assert.Equal(["allow"], _seen.Handled);
    }

    [Fact]
    public async Task AnInterceptorOfAPluginThatIsOff_IsSkipped()
    {
        await StartAsync(s => s.AddPluginEventInterceptor<Ping, RefusingInterceptor>("off"), "on");

        Assert.False((await Bus().InterceptAsync(new Ping("p"))).Cancelled);
    }

    [Fact]
    public async Task ABrokenOrSlowInterceptor_IsLogged_AndDoesNotBlockTheAction()
    {
        await StartAsync(s =>
        {
            s.AddPluginEventInterceptor<Ping, BrokenInterceptor>("a");
            s.AddPluginEventInterceptor<Ping, SlowInterceptor>("a");
        }, "a");

        var result = await Bus().InterceptAsync(new Ping("p"));

        Assert.False(result.Cancelled);
        Assert.Contains(CapturingLogger<PluginEventBus>.Warnings, w => w.Contains("BrokenInterceptor") && w.Contains("interceptor bug"));
        Assert.Contains(CapturingLogger<PluginEventBus>.Warnings, w => w.Contains("SlowInterceptor") && w.Contains("timed out"));
    }

    [Fact]
    public async Task TheDeliveryScope_GetsTheHostsRestoredState()
    {
        var restored = string.Empty;
        await StartAsync(s =>
        {
            s.AddPluginEventHandler<Ping, StateReadingHandler>("a");
            s.AddSingleton<Action<string?>>(v => restored = v ?? "null");
        }, "a");

        await Bus().PublishAsync(new Ping("s"));
        await _seen.WaitForAsync(1);

        Assert.Equal("state-a", restored);
    }

    private sealed class StateReadingHandler(Seen seen, RestoredState state, Action<string?> report) : IPluginEventHandler<Ping>
    {
        public Task HandleAsync(Ping pluginEvent, CancellationToken cancellationToken)
        {
            report(state.Value);
            seen.Note("state");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void TheWellKnownEvents_CarryOnlyPlainValues_NeverAnExceptionObject()
    {
        foreach (var type in new[] { typeof(ExceptionRecordedEvent), typeof(PostPublishedEvent), typeof(PostPublishingEvent) })
        {
            Assert.All(type.GetProperties(), p => Assert.False(typeof(Exception).IsAssignableFrom(p.PropertyType), $"{type.Name}.{p.Name} holds an exception"));
            Assert.True(typeof(IPluginEvent).IsAssignableFrom(type));
        }
    }
}
