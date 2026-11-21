namespace Adventures.MEF;

/// <summary>
/// Marks an event a plug-in can listen to. Events are immutable records of plain values (strings, numbers, dates): never an exception object, a
/// connection, a token or any other secret, so a handler written by someone else cannot read what it should not.
/// </summary>
public interface IPluginEvent
{
}

/// <summary>A plug-in's reaction to something that happened (a notification). It runs after the fact, off the request, isolated from every other handler.</summary>
public interface IPluginEventHandler<in TEvent>
    where TEvent : IPluginEvent
{
    Task HandleAsync(TEvent pluginEvent, CancellationToken cancellationToken);
}

/// <summary>A plug-in's say before something happens (an interceptor): awaited in order, it may refuse with a reason.</summary>
public interface IPluginEventInterceptor<in TEvent>
    where TEvent : IPluginEvent
{
    Task<InterceptResult> InterceptAsync(TEvent pluginEvent, CancellationToken cancellationToken);
}

/// <summary>An interceptor's answer: allow the action, or cancel it with a reason shown to the person who asked.</summary>
public sealed record InterceptResult(bool Cancelled, string? Reason)
{
    public static readonly InterceptResult Allow = new(false, null);

    public static InterceptResult Cancel(string reason) => new(true, reason);
}

/// <summary>Where code raises events. Raising never throws and never waits for a plug-in.</summary>
public interface IPluginEvents
{
    /// <summary>
    /// Raises a notification: the handlers of the plug-ins that are on for this tenant run later, off the request, one after another, each isolated and
    /// timed out. Returns at once. An event raised from inside a handler is ignored.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent pluginEvent, CancellationToken cancellationToken = default)
        where TEvent : IPluginEvent;

    /// <summary>
    /// Asks the interceptors of the plug-ins that are on for this tenant, in registration order, until one cancels. A broken or slow interceptor is
    /// logged and counts as allowing (so a plug-in bug cannot lock the site).
    /// </summary>
    Task<InterceptResult> InterceptAsync<TEvent>(TEvent pluginEvent, CancellationToken cancellationToken = default)
        where TEvent : IPluginEvent;
}

/// <summary>What the host remembers about the request when an event is raised (the tenant, and an opaque state it restores for the handlers).</summary>
public sealed record PluginEventSnapshot(string? Tenant, object? State = null);

/// <summary>
/// The host's part of delivering an event later: <see cref="Capture"/> runs in the request that raised it, <see cref="Restore"/> in the fresh scope the
/// handlers run in (so tenant-scoped services such as the plug-in settings answer for the right tenant).
/// </summary>
public interface IPluginEventContext
{
    PluginEventSnapshot Capture(IServiceProvider requestServices);

    void Restore(IServiceProvider deliveryServices, PluginEventSnapshot snapshot);
}

/// <summary>Who an event was raised for, available to a handler while it runs.</summary>
public sealed class PluginEventOrigin
{
    public string? Tenant { get; internal set; }
}

/// <summary>Limits of the event bus.</summary>
public sealed class PluginEventOptions
{
    /// <summary>How many deliveries wait for the background worker; more than this are dropped and logged, never blocking a request.</summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>The longest a notification handler may take before it is given up on.</summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The longest an interceptor may take (a request is waiting for it).</summary>
    public TimeSpan InterceptorTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>An exception was written to the exception log. Plain text only, scrubbed of secrets and shortened.</summary>
public sealed record ExceptionRecordedEvent(string EntryId, string ExceptionType, string Message, string? CorrelationId, DateTimeOffset At) : IPluginEvent;

/// <summary>A post became visible to readers.</summary>
public sealed record PostPublishedEvent(string PostId, string Slug, string Title, DateTimeOffset At) : IPluginEvent;

/// <summary>A post is about to be published (an interceptor may refuse).</summary>
public sealed record PostPublishingEvent(string PostId, string Slug, string Title) : IPluginEvent;
