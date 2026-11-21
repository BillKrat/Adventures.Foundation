using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventures.MEF;

/// <summary>One handler or interceptor a plug-in registered: which plug-in it belongs to (so it is skipped when that plug-in is off), for which event, and its type.</summary>
public sealed record PluginEventRegistration(string PluginKey, Type EventType, Type ImplementationType, bool IsInterceptor);

/// <summary>Registration of the bus and of a plug-in's handlers and interceptors (called from a plug-in's <see cref="IPluginModule.ConfigureServices"/>).</summary>
public static class PluginEventServiceCollectionExtensions
{
    /// <summary>Adds the bus: the publisher, the bounded queue, and the background worker that delivers notifications. Safe to call more than once.</summary>
    public static IServiceCollection AddPluginEvents(this IServiceCollection services, Action<PluginEventOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new PluginEventOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(new PluginEventQueue(options.QueueCapacity));
        services.TryAddScoped<PluginEventOrigin>();
        services.TryAddScoped<IPluginEvents, PluginEventBus>();
        if (!services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PluginEventDispatcher)))
        {
            services.AddSingleton<IHostedService, PluginEventDispatcher>();
        }

        return services;
    }

    /// <summary>Registers <typeparamref name="THandler"/> to hear <typeparamref name="TEvent"/> for the plug-in <paramref name="pluginKey"/>; it runs only while that plug-in is on.</summary>
    public static IServiceCollection AddPluginEventHandler<TEvent, THandler>(this IServiceCollection services, string pluginKey)
        where TEvent : IPluginEvent
        where THandler : class, IPluginEventHandler<TEvent> =>
        Register<TEvent, THandler>(services, pluginKey, interceptor: false);

    /// <summary>Registers <typeparamref name="TInterceptor"/> to be asked before <typeparamref name="TEvent"/> happens, for the plug-in <paramref name="pluginKey"/>.</summary>
    public static IServiceCollection AddPluginEventInterceptor<TEvent, TInterceptor>(this IServiceCollection services, string pluginKey)
        where TEvent : IPluginEvent
        where TInterceptor : class, IPluginEventInterceptor<TEvent> =>
        Register<TEvent, TInterceptor>(services, pluginKey, interceptor: true);

    private static IServiceCollection Register<TEvent, TImpl>(IServiceCollection services, string pluginKey, bool interceptor)
        where TEvent : IPluginEvent
        where TImpl : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginKey);
        services.AddScoped<TImpl>();
        services.AddSingleton(new PluginEventRegistration(pluginKey, typeof(TEvent), typeof(TImpl), interceptor));
        return services;
    }
}

/// <summary>What waits for the background worker: the event, who should get it, and what the host captured.</summary>
public sealed record PluginEventDelivery(object Event, IReadOnlyList<PluginEventRegistration> Targets, PluginEventSnapshot Snapshot);

/// <summary>The bounded queue between a request that raises an event and the worker that delivers it. Full means drop and log, never wait.</summary>
public sealed class PluginEventQueue(int capacity)
{
    private readonly Channel<PluginEventDelivery> _channel = Channel.CreateBounded<PluginEventDelivery>(new BoundedChannelOptions(Math.Max(1, capacity))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    public ChannelReader<PluginEventDelivery> Reader => _channel.Reader;

    public bool TryWrite(PluginEventDelivery delivery) => _channel.Writer.TryWrite(delivery);
}

/// <summary>Marks the code that runs inside a handler, so an event raised from there is ignored (a failing plug-in cannot trigger itself).</summary>
internal static class PluginEventGuard
{
    private static readonly AsyncLocal<bool> InsideHandler = new();

    public static bool IsInsideHandler => InsideHandler.Value;

    public static IDisposable Enter()
    {
        InsideHandler.Value = true;
        return new Leave();
    }

    private sealed class Leave : IDisposable
    {
        public void Dispose() => InsideHandler.Value = false;
    }
}

/// <summary>Raises events for the request in which it is resolved (see <see cref="IPluginEvents"/>).</summary>
public sealed class PluginEventBus(
    IServiceProvider services,
    IEnumerable<PluginEventRegistration> registrations,
    PluginEventQueue queue,
    PluginEventOptions options,
    ILogger<PluginEventBus>? logger = null) : IPluginEvents
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task PublishAsync<TEvent>(TEvent pluginEvent, CancellationToken cancellationToken = default)
        where TEvent : IPluginEvent
    {
        ArgumentNullException.ThrowIfNull(pluginEvent);
        if (PluginEventGuard.IsInsideHandler)
        {
            return;
        }

        var targets = await EnabledAsync(typeof(TEvent), interceptors: false, cancellationToken).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            return;
        }

        var snapshot = services.GetService<IPluginEventContext>()?.Capture(services) ?? new PluginEventSnapshot(null);
        if (!queue.TryWrite(new PluginEventDelivery(pluginEvent, targets, snapshot)))
        {
            _logger.LogWarning("The plug-in event queue is full: an event of {EventType} was dropped", typeof(TEvent).Name);
        }
    }

    public async Task<InterceptResult> InterceptAsync<TEvent>(TEvent pluginEvent, CancellationToken cancellationToken = default)
        where TEvent : IPluginEvent
    {
        ArgumentNullException.ThrowIfNull(pluginEvent);
        if (PluginEventGuard.IsInsideHandler)
        {
            return InterceptResult.Allow;
        }

        foreach (var target in await EnabledAsync(typeof(TEvent), interceptors: true, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var interceptor = (IPluginEventInterceptor<TEvent>)services.GetRequiredService(target.ImplementationType);
                using var guard = PluginEventGuard.Enter();
                var result = await interceptor.InterceptAsync(pluginEvent, cancellationToken).WaitAsync(options.InterceptorTimeout, cancellationToken).ConfigureAwait(false);
                if (result.Cancelled)
                {
                    return result;
                }
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("The plug-in interceptor {Interceptor} of plug-in {Plugin} timed out and was treated as allowing", target.ImplementationType.Name, target.PluginKey);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("The plug-in interceptor {Interceptor} of plug-in {Plugin} failed and was treated as allowing: {Reason}", target.ImplementationType.Name, target.PluginKey, ex.Message);
            }
        }

        return InterceptResult.Allow;
    }

    /// <summary>The registrations for this event type whose plug-in is on for the tenant of this request (all of them when no plug-in state is registered).</summary>
    private async Task<IReadOnlyList<PluginEventRegistration>> EnabledAsync(Type eventType, bool interceptors, CancellationToken cancellationToken)
    {
        var candidates = registrations.Where(r => r.EventType == eventType && r.IsInterceptor == interceptors).ToList();
        if (candidates.Count == 0)
        {
            return candidates;
        }

        try
        {
            if (services.GetService<IPlugins>() is { } plugins)
            {
                var on = await plugins.EnabledKeysAsync(cancellationToken).ConfigureAwait(false);
                return candidates.Where(r => on.Contains(r.PluginKey)).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If the switches cannot be read, no plug-in code runs: unknown means off.
            _logger.LogWarning("Plug-in switches could not be read, so no plug-in event was delivered: {Reason}", ex.Message);
            return [];
        }

        return candidates;
    }
}

/// <summary>Delivers queued notifications: a fresh scope per event, the host's state restored, each handler isolated and timed out, no event raised from inside.</summary>
public sealed class PluginEventDispatcher(
    IServiceProvider services,
    PluginEventQueue queue,
    PluginEventOptions options,
    ILogger<PluginEventDispatcher>? logger = null) : BackgroundService
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var delivery in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DeliverAsync(delivery, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    private async Task DeliverAsync(PluginEventDelivery delivery, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<PluginEventOrigin>().Tenant = delivery.Snapshot.Tenant;
            scope.ServiceProvider.GetService<IPluginEventContext>()?.Restore(scope.ServiceProvider, delivery.Snapshot);
            foreach (var target in delivery.Targets)
            {
                await RunAsync(scope.ServiceProvider, target, delivery.Event, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Delivering a plug-in event failed: {Reason}", ex.Message);
        }
    }

    private async Task RunAsync(IServiceProvider scope, PluginEventRegistration target, object pluginEvent, CancellationToken stoppingToken)
    {
        try
        {
            var handler = scope.GetRequiredService(target.ImplementationType);
            var method = typeof(IPluginEventHandler<>).MakeGenericType(target.EventType).GetMethod(nameof(IPluginEventHandler<IPluginEvent>.HandleAsync))!;
            using var guard = PluginEventGuard.Enter();
            await ((Task)method.Invoke(handler, [pluginEvent, stoppingToken])!).WaitAsync(options.HandlerTimeout, stoppingToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("The plug-in handler {Handler} of plug-in {Plugin} timed out and was given up on", target.ImplementationType.Name, target.PluginKey);
        }
        catch (TargetInvocationException ex)
        {
            _logger.LogWarning("The plug-in handler {Handler} of plug-in {Plugin} failed: {Reason}", target.ImplementationType.Name, target.PluginKey, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("The plug-in handler {Handler} of plug-in {Plugin} failed: {Reason}", target.ImplementationType.Name, target.PluginKey, ex.Message);
        }
    }
}
