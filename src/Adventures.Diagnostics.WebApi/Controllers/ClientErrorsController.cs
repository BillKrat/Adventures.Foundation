// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Diagnostics.WebApi;

public sealed record ClientErrorReport(string? Message, string? Stack, string? Url);

/// <summary>
/// Where the browser reports an error it caught (no silent errors: a failure that never reached the server would
/// otherwise live only in one person's console). Open to anonymous callers too, because the errors that matter most
/// can happen before anyone is signed in (the login page itself). Since anyone can call it, it is bounded: each kind
/// of error is recorded at most once per ten minutes, signed-in callers are capped per minute, and anonymous callers
/// get a smaller per-minute cap plus a daily cap, so it cannot be used to fill the store. A signed-in caller's name is
/// recorded; an anonymous report carries none.
/// </summary>
[ApiController]
[Route("api/client-errors")]
[AllowAnonymous]
public sealed class ClientErrorsController(IExceptionLogBll exceptionLog, ILogger<ClientErrorsController> logger) : ControllerBase
{
    public static readonly TimeSpan SameErrorWindow = TimeSpan.FromMinutes(10);
    public const int MaxPerMinuteSignedIn = 30;
    public const int MaxPerMinuteAnonymous = 10;
    public const int MaxAnonymousPerDay = 200;
    private const int MaxText = 2_000;
    private static readonly object Gate = new();
    private static DateTimeOffset _minuteStart = DateTimeOffset.MinValue;
    private static int _signedInInMinute;
    private static int _anonymousInMinute;
    private static DateTimeOffset _dayStart = DateTimeOffset.MinValue;
    private static int _anonymousInDay;

    /// <summary>Clears the rate counters (tests only).</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            _minuteStart = _dayStart = DateTimeOffset.MinValue;
            _signedInInMinute = _anonymousInMinute = _anonymousInDay = 0;
        }
    }

    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ClientErrorReport report, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(report.Message))
        {
            return BadRequest("A message is required.");
        }

        var signedIn = User.Identity?.IsAuthenticated == true;
        if (!TryCount(signedIn))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var correlationId = Adventures.WebApi.CorrelationIdMiddleware.Get(HttpContext);
        var url = Limit(report.Url) ?? "(unknown page)";
        var exception = new ClientReportedException(Limit(report.Message)!, Limit(report.Stack));
        logger.LogWarning("Client error on {Url}: {Message} (signed in: {SignedIn})", url, exception.Message, signedIn);

        var id = await exceptionLog.RecordThrottledAsync(
            exception, $"client: {url}", signedIn ? User.Identity?.Name : null, correlationId, SameErrorWindow, cancellationToken);
        return Accepted(new { errorId = id, correlationId });
    }

    private static string? Limit(string? text) => text is { Length: > MaxText } ? text[..MaxText] : text;

    private static bool TryCount(bool signedIn)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _minuteStart >= TimeSpan.FromMinutes(1))
            {
                _minuteStart = now;
                _signedInInMinute = 0;
                _anonymousInMinute = 0;
            }

            if (signedIn)
            {
                return ++_signedInInMinute <= MaxPerMinuteSignedIn;
            }

            if (now - _dayStart >= TimeSpan.FromDays(1))
            {
                _dayStart = now;
                _anonymousInDay = 0;
            }

            return ++_anonymousInMinute <= MaxPerMinuteAnonymous && ++_anonymousInDay <= MaxAnonymousPerDay;
        }
    }
}
