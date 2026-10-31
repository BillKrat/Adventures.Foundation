// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adventures.WebApi;

/// <summary>
/// The host's last-resort handler for an exception no controller or Bll answered. It does three things, in order:
/// writes the full exception (message, stack trace, inner exceptions) to the application log file; records it in the
/// exception log, where an administrator reviews and purges it; and answers the client with a shielded body - a friendly
/// title and a reference to the log entry, never the message or the stack (AppArch: exception shielding). Logging must
/// never make things worse, so a failure to record is itself only logged.
/// </summary>
public static class UnhandledExceptionHandler
{
    public const string FriendlyTitle = "Something went wrong on the server. It has been logged; please tell an administrator.";

    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var request = $"{context.Request.Method} {context.Request.Path}";

        string? errorId = null;
        if (exception is not null && exception is not OperationCanceledException)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(UnhandledExceptionHandler))
                .LogError(exception, "Unhandled exception handling {Request}", request);

            var exceptionLog = context.RequestServices.GetService<IExceptionLogBll>();
            if (exceptionLog is not null)
            {
                errorId = await exceptionLog.RecordAsync(
                    exception, request, context.User.Identity?.Name, CorrelationIdMiddleware.Get(context));
            }
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = 500,
            title = FriendlyTitle,
            errorId,
            correlationId = CorrelationIdMiddleware.Get(context),
        });
    }
}
