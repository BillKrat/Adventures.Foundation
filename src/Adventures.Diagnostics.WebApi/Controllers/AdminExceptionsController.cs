// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.WebApi;
using Adventures.Common.Diagnostics;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Diagnostics.WebApi;

/// <summary>Active exceptions (what the warning icon counts) and all of them, purged ones included (the history worth opening).</summary>
public sealed record ExceptionCount(int Count, int Total);

public sealed record PurgeResult(int Purged);

public sealed record SignatureStatusChange(string? Status);

/// <summary>
/// The administrator's view of the exception log (see <see cref="UnhandledExceptionHandler"/>): the active count that
/// drives the warning icon, the grid, and purge. Purge soft-deletes every entry, like any other delete, so the history
/// stays in the store. Administrators only; this area later moves onto the Dashboard.
/// </summary>
[ApiController]
[Route("api/admin/exceptions")]
[RequireRight(Rights.DiagnosticsRead)]
public sealed class AdminExceptionsController(IExceptionLogPresenter presenter, IExceptionSignatureBll signatures) : ControllerBase
{
    /// <summary>The kinds of failure seen (grouped repeats), most recently seen first, each with how often.</summary>
    [HttpGet("signatures")]
    public async Task<ActionResult<IReadOnlyList<ExceptionSignatureSummary>>> Signatures(CancellationToken cancellationToken)
    {
        // The stored counter can drift from the log; the number on the pill is what the drill-down lists (history included).
        var kinds = await signatures.ListSummariesAsync(cancellationToken);
        var logged = await presenter.GetHistoryGridAsync(cancellationToken);
        return Ok(WithLoggedCounts(kinds, logged.Rows));
    }

    /// <summary>Each kind's count becomes the number of logged occurrences carrying its signature id (purged ones included); a kind with none logged keeps its stored count.</summary>
    public static IReadOnlyList<ExceptionSignatureSummary> WithLoggedCounts(IReadOnlyList<ExceptionSignatureSummary> kinds, IReadOnlyList<EntityDataModel> rows)
    {
        var perKind = rows
            .Select(row => row.Values.GetValueOrDefault("SignatureId"))
            .Where(id => !string.IsNullOrEmpty(id))
            .GroupBy(id => id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return kinds.Select(kind => perKind.TryGetValue(kind.Id, out var count) ? kind with { OccurrenceCount = count } : kind).ToList();
    }

/// <summary>An administrator's triage of one kind of failure: Acknowledged (being handled), Fixed, or back to New. A Fixed kind that recurs becomes Regressed by itself.</summary>    [HttpPut("signatures/{signatureId}/status")]    public async Task<IActionResult> SetStatus(string signatureId, [FromBody] SignatureStatusChange change, CancellationToken cancellationToken)    {        if (!ExceptionSignatureStatus.Settable.Contains(change.Status ?? string.Empty, StringComparer.Ordinal))        {            return BadRequest(new { title = $"Status must be one of: {string.Join(", ", ExceptionSignatureStatus.Settable)}." });        }        return await signatures.SetStatusAsync(signatureId, change.Status!, cancellationToken) ? NoContent() : NotFound();    }
    /// <summary>
    /// The active exceptions, newest first; <c>?includePurged=true</c> adds the purged ones (soft deleted, each marked in its State column);
    /// <c>?signature=&lt;id&gt;</c> drills into one kind of failure (see Kinds of failure) and lists only its occurrences.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<EntityGridModel>> Grid(
        [FromQuery] bool includePurged = false,
        [FromQuery] string? signature = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = EntityGridQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var all = !string.IsNullOrWhiteSpace(signature)
            ? await presenter.GetSignatureGridAsync(signature, includePurged, cancellationToken)
            : includePurged ? await presenter.GetHistoryGridAsync(cancellationToken) : await presenter.GetGridAsync(cancellationToken);

        // Paged like every other grid (25 a page, at most 100); a page past the end shows the last one. The next and previous links
        // keep the history switch and the drill-down, which the generic link builder does not know about.
        var query = new EntityGridQuery(page, pageSize).Normalized();
        var paging = new GridPaging(query.Page, query.PageSize, all.Rows.Count, null, false);
        var current = Math.Min(query.Page, paging.TotalPages);
        paging = paging with { Page = current };

        string Link(int number) =>
            $"/api/admin/exceptions?page={number}&pageSize={paging.PageSize}"
            + (includePurged ? "&includePurged=true" : string.Empty)
            + (string.IsNullOrWhiteSpace(signature) ? string.Empty : $"&signature={Uri.EscapeDataString(signature)}");

        var rows = all.Rows.Skip((current - 1) * paging.PageSize).Take(paging.PageSize).ToArray();
        var links = new EntityGridLinks(
            Link(current), null, current < paging.TotalPages ? Link(current + 1) : null, current > 1 ? Link(current - 1) : null);
        return Ok(all with { Rows = rows, Paging = paging, Links = links });
    }

    [HttpGet("count")]
    public async Task<ActionResult<ExceptionCount>> Count(CancellationToken cancellationToken) =>
        Ok(new ExceptionCount(await presenter.CountActiveAsync(cancellationToken), await presenter.CountTotalAsync(cancellationToken)));

    [RequireRight(Rights.SettingsManage)]
    [HttpDelete]
    public async Task<ActionResult<PurgeResult>> Purge(CancellationToken cancellationToken) =>
        Ok(new PurgeResult(await presenter.PurgeAsync(cancellationToken)));
}
