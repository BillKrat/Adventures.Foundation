// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Diagnostics;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Diagnostics.WebApi;

/// <summary>Everything known about one request: its recorded exceptions, their kinds of failure, and its log lines.</summary>
public sealed record RequestDiagnostics(CorrelationReport Report, IReadOnlyList<string> LogLines);

/// <summary>What both diagnostics controllers answer with, so an administrator and an MCP tool see the same thing.</summary>
internal static class DiagnosticsLookup
{
    public const string BadIdMessage = "The correlation id may only contain letters, digits, '-' and '_' (at most 64 characters).";

    public static async Task<RequestDiagnostics> ForRequestAsync(IDiagnosticsReader reader, string correlationId, CancellationToken cancellationToken)
    {
        var report = await reader.FindByCorrelationIdAsync(correlationId, cancellationToken);
        var lines = await LogSearch.FindLinesAsync(Path.Combine(AppContext.BaseDirectory, "logs"), correlationId, cancellationToken: cancellationToken);
        return new RequestDiagnostics(report, lines);
    }
}

/// <summary>
/// Eyes into the running system for an administrator: given a CorrelationId (shown in every error message and on every
/// response) return what the system recorded for that request. Read-only; administrators only. The same answers for a
/// machine caller are at <see cref="McpDiagnosticsController"/>.
/// </summary>
[ApiController]
[Route("api/admin/diagnostics")]
[RequireRight(Rights.DiagnosticsRead)]
public sealed class AdminDiagnosticsController(IDiagnosticsReader reader, IDependencyProbes probes, IAiTriage aiTriage) : ControllerBase
{
    /// <summary>
    /// "Ask Claude": the AI's assessment of one request. A POST because it costs money and sends a redacted report to
    /// Anthropic, so it only ever runs from an explicit click; cached an hour per request and capped per hour in all.
    /// Answers 200 with <c>Available: false</c> and a reason when the AI is not set up or unreachable.
    /// </summary>
    [RequireRight(Rights.DiagnosticsAi)]
    [HttpPost("{correlationId}/ai-triage")]
    public async Task<ActionResult<AiTriageResult>> AskClaude(string correlationId, CancellationToken cancellationToken)
    {
        if (!Adventures.WebApi.CorrelationIdMiddleware.IsWellFormed(correlationId))
        {
            return BadRequest(DiagnosticsLookup.BadIdMessage);
        }

        var found = await DiagnosticsLookup.ForRequestAsync(reader, correlationId, cancellationToken);
        var rules = await Triage.RunAsync(found.Report, found.LogLines, probes, DateTimeOffset.UtcNow, cancellationToken);
        return Ok(await aiTriage.AskAsync(found.Report, found.LogLines, rules, cancellationToken));
    }

    /// <summary>
    /// The Diagnose button: triage of one request by its CorrelationId (what happened, whether it is still happening
    /// through a live check, what the log says, what to do next, and the command that hands it to Claude). Read-only.
    /// </summary>
    [HttpGet("{correlationId}/triage")]
    public async Task<ActionResult<TriageResult>> TriageRequest(string correlationId, CancellationToken cancellationToken)
    {
        if (!Adventures.WebApi.CorrelationIdMiddleware.IsWellFormed(correlationId))
        {
            return BadRequest(DiagnosticsLookup.BadIdMessage);
        }

        var found = await DiagnosticsLookup.ForRequestAsync(reader, correlationId, cancellationToken);
        return Ok(await Triage.RunAsync(found.Report, found.LogLines, probes, DateTimeOffset.UtcNow, cancellationToken));
    }

    [HttpGet("{correlationId}")]
    public async Task<ActionResult<RequestDiagnostics>> ForRequest(string correlationId, CancellationToken cancellationToken) =>
        Adventures.WebApi.CorrelationIdMiddleware.IsWellFormed(correlationId)
            ? Ok(await DiagnosticsLookup.ForRequestAsync(reader, correlationId, cancellationToken))
            : BadRequest(DiagnosticsLookup.BadIdMessage);

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<ExceptionOccurrence>>> Recent([FromQuery] int max = 50, CancellationToken cancellationToken = default) =>
        Ok(await reader.ListRecentAsync(max, cancellationToken));
}

/// <summary>
/// The machine surface of the diagnostics: a token with the <c>mcp.diagnostics.read</c> scope (issued to the
/// <c>claude-diagnostics</c> client by /api/auth/m2m/token) reads the same data, read-only. The MCP server's diagnostics
/// tool forwards to these routes.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
[Authorize(Policy = "Scope:" + McpDiagnosticsController.RequiredScope)]
public sealed class McpDiagnosticsController(IDiagnosticsReader reader) : ControllerBase
{
    public const string RequiredScope = "mcp.diagnostics.read";

    [HttpGet("{correlationId}")]
    public async Task<ActionResult<RequestDiagnostics>> ForRequest(string correlationId, CancellationToken cancellationToken) =>
        Adventures.WebApi.CorrelationIdMiddleware.IsWellFormed(correlationId)
            ? Ok(await DiagnosticsLookup.ForRequestAsync(reader, correlationId, cancellationToken))
            : BadRequest(DiagnosticsLookup.BadIdMessage);

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<ExceptionOccurrence>>> Recent([FromQuery] int max = 50, CancellationToken cancellationToken = default) =>
        Ok(await reader.ListRecentAsync(max, cancellationToken));

    [HttpGet("signatures")]
    public async Task<ActionResult<IReadOnlyList<ExceptionSignatureSummary>>> Signatures(CancellationToken cancellationToken) =>
        Ok(await reader.ListSignaturesAsync(cancellationToken));
}
