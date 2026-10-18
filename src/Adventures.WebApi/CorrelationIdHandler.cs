using Microsoft.AspNetCore.Http;

namespace Adventures.WebApi;

/// <summary>
/// Outbound half of the CorrelationId: copies the current request's id onto calls this site makes to another site
/// (for example API to MCP), so the receiving site logs the same id. OpenTelemetry's <c>traceparent</c> already links the
/// traces; this header also covers receivers that do not run OpenTelemetry. Does nothing outside a request.
/// </summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = accessor.HttpContext is { } context ? CorrelationIdMiddleware.Get(context) : null;
        if (id is not null && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
