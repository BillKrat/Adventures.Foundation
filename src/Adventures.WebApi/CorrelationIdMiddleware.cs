using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Adventures.Common.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Adventures.WebApi;

/// <summary>
/// Gives every request one CorrelationId and carries it everywhere the request goes: into every log line (as a logging scope,
/// so Serilog and any other provider see it as the <c>CorrelationId</c> property), onto the request's <see cref="Activity"/>
/// (tag <c>correlation.id</c>, so the Aspire/OpenTelemetry traces find it), into <see cref="HttpContext.Items"/> for the
/// exception log, and back to the caller in the <c>X-Correlation-Id</c> response header (error responses included).
/// The id is the caller's <c>X-Correlation-Id</c> when it sent a well-formed one (Angular, another site, a tool); otherwise the
/// W3C trace id OpenTelemetry already created for the request; otherwise a new GUID. Run it first in the pipeline.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = CorrelationIds.HeaderName;
    public const string ItemsKey = "Adventures.CorrelationId";
    public const string LogPropertyName = CorrelationIds.LogPropertyName;
    public const string ActivityTagName = CorrelationIds.ActivityTagName;

    /// <summary>The correlation id of the request, or null when the middleware did not run.</summary>
    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemsKey, out var value) ? value as string : null;

    /// <inheritdoc cref="CorrelationIds.IsWellFormed"/>
    public static bool IsWellFormed(string? candidate) => CorrelationIds.IsWellFormed(candidate);

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var id = IsWellFormed(incoming)
            ? incoming
            : Activity.Current?.TraceId.ToString() is { Length: > 0 } traceId && traceId != "00000000000000000000000000000000"
                ? traceId
                : Guid.NewGuid().ToString("N");

        context.Items[ItemsKey] = id;
        Activity.Current?.SetTag(ActivityTagName, id);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { [LogPropertyName] = id }))
        {
            await next(context);
        }
    }
}
