using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adventures.WebApi;

/// <summary>
/// A visitor who leaves while the server is still working is not a fault, so it does not reach the exception log. The server notices in the work it
/// cancels, and a database driver reports that as an error of its own with the cancellation inside it. Such a request is answered 499 ("client
/// closed request") and nothing is recorded. Both conditions must hold: the visitor has left, and the error comes from a cancellation. Any other
/// error, and the same error while the visitor is still waiting (a real time-out), goes to the exception handler as before.
/// </summary>
public static class AbandonedRequests
{
    public const int ClientClosedRequest = 499;

    /// <summary>Whether <paramref name="exception"/> is only the sound of work being cancelled because the visitor left.</summary>
    public static bool IsAbandoned(HttpContext context, Exception exception)
    {
        if (!context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        for (var cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sets an abandoned request aside. Place it after the exception handler, so it answers first.</summary>
    public static IApplicationBuilder UseAbandonedRequests(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception) when (IsAbandoned(context, exception))
            {
                context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(AbandonedRequests))
                    .LogDebug(exception, "The visitor left before {Method} {Path} was answered", context.Request.Method, context.Request.Path);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = ClientClosedRequest;
                }
            }
        });

    /// <summary>The host's one exception handler (see <see cref="UnhandledExceptionHandler"/>), with abandoned requests set aside before it.</summary>
    public static IApplicationBuilder UseShieldedErrors(this IApplicationBuilder app) =>
        app.UseExceptionHandler(errorApp => errorApp.Run(UnhandledExceptionHandler.HandleAsync))
            .UseAbandonedRequests();
}
