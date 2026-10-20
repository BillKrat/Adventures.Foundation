using System.Globalization;
using System.Text.Json;

namespace Adventures.Common.Diagnostics;

/// <summary>One thing the triage found out; <c>Severity</c> is info, warning or problem.</summary>
public sealed record TriageFinding(string Title, string Detail, string Severity);

/// <summary>
/// The answer of the Diagnose button: a verdict in one line, the findings behind it, what to do next, and the exact
/// command that hands the same request to Claude (who reads the same evidence through the MCP diagnostics tool and goes further).
/// </summary>
public sealed record TriageResult(
    string CorrelationId,
    string Verdict,
    string Summary,
    IReadOnlyList<TriageFinding> Findings,
    IReadOnlyList<string> NextSteps,
    string HandOffCommand);

/// <summary>
/// Rule-based triage of one request, by CorrelationId. It is deliberately small: each kind of exception we meet earns a
/// rule (what it usually means, a live check that tells "still happening" from "already recovered", what to do). An
/// exception no rule knows still gets its facts (what, when, how often, what the log says) and the hand-off to Claude.
/// Read-only: the live checks only look.
/// </summary>
public static class Triage
{
    public const string ProbeRequestMcp = "health probe: mcp";
    public const string ProbeRequestDatabase = "health probe: database";

    public static string HandOff(string correlationId) =>
        $".\\docs\\Claude-secret.ps1 run m2m/claude-diagnostics/claude-diagnostics -EnvVar CLAUDE_DIAG_SECRET -Exec powershell -ExecArgs \"-NoProfile\",\"-File\",\".\\docs\\Claude-diagnose.ps1\",\"-CorrelationId\",\"{correlationId}\"";

    public static async Task<TriageResult> RunAsync(
        CorrelationReport report, IReadOnlyList<string> logLines, IDependencyProbes probes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var findings = new List<TriageFinding>();
        var next = new List<string>();
        var logFindings = AnalyseLog(logLines).ToList();

        if (report.Exceptions.Count == 0)
        {
            findings.AddRange(logFindings);
            var summary = logLines.Count == 0
                ? "Nothing is recorded for this id: no exception and no log lines (it may be older than the 14 retained days, or from another site)."
                : "No exception was recorded for this request; its log lines are summarised below.";
            next.Add("If the user saw an error for this id, check the id was copied whole, and ask Claude to look (hand-off below).");
            return new TriageResult(report.CorrelationId, "Nothing recorded", summary, findings, next, HandOff(report.CorrelationId));
        }

        var verdict = "Needs attention";
        var headline = string.Empty;
        foreach (var occurrence in report.Exceptions)
        {
            var kind = report.Signatures.FirstOrDefault(s => s.Id == occurrence.SignatureId);
            findings.Add(new TriageFinding(
                "What happened",
                $"{occurrence.ExceptionType}: {Trim(occurrence.Message, 300)} (request: {occurrence.Request ?? "unknown"}, user: {occurrence.UserName ?? "none"}, at {occurrence.OccurredAt:u}).",
                "info"));

            if (kind is not null)
            {
                var recent = kind.LastSeen is { } last && now - last < TimeSpan.FromHours(1);
                findings.Add(new TriageFinding(
                    "History of this kind of failure",
                    $"Seen {kind.OccurrenceCount} time(s), first {kind.FirstSeen:u}, last {kind.LastSeen:u}; {(recent ? "it happened within the last hour" : "not seen in the last hour")}. Status: {kind.Status}.",
                    recent ? "warning" : "info"));
            }

            (verdict, headline) = await ApplyRulesAsync(occurrence, probes, findings, next, cancellationToken);
        }

        findings.AddRange(logFindings);
        if (next.Count == 0)
        {
            next.Add("No rule covers this exception yet: hand it to Claude (command below); once understood, add a rule to Triage.cs so the button answers it next time.");
        }

        return new TriageResult(report.CorrelationId, verdict, headline, findings, next, HandOff(report.CorrelationId));
    }

    private static async Task<(string Verdict, string Headline)> ApplyRulesAsync(
        ExceptionOccurrence occurrence, IDependencyProbes probes, List<TriageFinding> findings, List<string> next, CancellationToken cancellationToken)
    {
        var request = occurrence.Request ?? string.Empty;
        var text = $"{occurrence.ExceptionType} {occurrence.Message}";

        if (request.StartsWith(ProbeRequestMcp, StringComparison.OrdinalIgnoreCase))
        {
            var live = await probes.ProbeMcpAsync(cancellationToken);
            var why = text.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase)
                ? "the TLS handshake with the mcp site failed (no certificate on the hostname, or the site was down)"
                : text.Contains("Timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                    ? "the mcp site did not answer in time"
                    : text.Contains("401", StringComparison.Ordinal)
                        ? "the mcp site refused the API's token (the Jwt signing key differs between the two sites)"
                        : "the API could not get a good answer from the mcp site";
            findings.Add(new TriageFinding("Likely cause", $"This is the API's health probe of the mcp site: {why}.", "info"));
            findings.Add(new TriageFinding(
                "Live check now",
                live.Ok ? $"mcp answers correctly ({live.DurationMs} ms): the outage is over."
                : live.Detail == "http_401" ? $"mcp is reachable over TLS ({live.DurationMs} ms) but rejects this API's token: the Jwt signing keys differ between the two sites (expected when this runs on a developer machine, a real fault in production)."
                : $"mcp still fails ({live.Detail}, {live.DurationMs} ms).",
                live.Ok ? "info" : "problem"));
            if (live.Ok)
            {
                next.Add("Nothing to fix now. Purge the entry once you have read it; if it keeps coming back, look at what changed on the mcp site (deploy, pool recycle, certificate).");
                return ("Recovered", "A past mcp outage; the live check passes now.");
            }

            next.Add("Check the mcp site: its certificate (SSL status in the panel), that the site is started, and that Jwt__SigningKey matches the API's.");
            return ("Ongoing", "mcp is still failing its health probe.");
        }

        if (request.StartsWith(ProbeRequestDatabase, StringComparison.OrdinalIgnoreCase))
        {
            var live = await probes.ProbeDatabaseAsync(cancellationToken);
            findings.Add(new TriageFinding(
                "Live check now",
                live.Ok ? $"The database answers ({live.DurationMs} ms): the failure is over." : $"The database still fails ({live.Detail}).",
                live.Ok ? "info" : "problem"));
            next.Add(live.Ok
                ? "Nothing to fix now; if it recurs, check the host's database status and the connection secret."
                : "Check the Postgres host status and the ConnectionStrings__Postgres secret on the api site (a rotated password is the usual cause).");
            return live.Ok ? ("Recovered", "A past database failure; the live check passes now.") : ("Ongoing", "The database is still failing.");
        }

        if (occurrence.ExceptionType.EndsWith("ClientReportedException", StringComparison.Ordinal))
        {
            findings.Add(new TriageFinding("Likely cause", $"An error in the browser ({request}); the message and stack are the browser's own. If it began after a deploy, the new UI code is the suspect.", "info"));
            next.Add("Reproduce on the page named in the request; compare with the last deploy; hand to Claude for the stack.");
            return ("Needs attention", "A browser-side error.");
        }

        if (occurrence.ExceptionType.Contains("TaskCanceled", StringComparison.Ordinal) || text.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new TriageFinding("Likely cause", "A call took longer than its limit (a slow or unreachable dependency).", "warning"));
            next.Add("Find which dependency the request called (the log lines below name it) and check it.");
            return ("Needs attention", "A timeout; see the log lines for what was being called.");
        }

        findings.Add(new TriageFinding("Rule", "No rule knows this exception yet.", "info"));
        return ("Needs attention", $"{occurrence.ExceptionType}: {Trim(occurrence.Message, 120)}");
    }

    /// <summary>Counts and highlights from the request's log lines (compact JSON): errors, warnings, slow responses.</summary>
    private static IEnumerable<TriageFinding> AnalyseLog(IReadOnlyList<string> logLines)
    {
        if (logLines.Count == 0)
        {
            yield break;
        }

        int errors = 0, warnings = 0;
        string? firstProblem = null;
        long slowest = 0;
        foreach (var line in logLines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var level = root.TryGetProperty("@l", out var l) ? l.GetString() : "Information";
                var message = root.TryGetProperty("@m", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                if (level == "Error")
                {
                    errors++;
                    firstProblem ??= message;
                }
                else if (level == "Warning")
                {
                    warnings++;
                    firstProblem ??= message;
                }

                var at = message.IndexOf(" in ", StringComparison.Ordinal);
                if (message.Contains("responded", StringComparison.Ordinal) && at > 0 && message.EndsWith(" ms", StringComparison.Ordinal))
                {
                    var number = message[(at + 4)..^3].Trim();
                    if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                    {
                        slowest = Math.Max(slowest, (long)ms);
                    }
                }
            }
            catch (JsonException)
            {
                // a stub or partial line: still counted as a line, not analysed
            }
        }

        yield return new TriageFinding(
            "Log lines of this request",
            $"{logLines.Count} line(s): {errors} error(s), {warnings} warning(s).{(firstProblem is null ? string.Empty : $" First problem: {Trim(firstProblem, 200)}")}",
            errors > 0 ? "problem" : warnings > 0 ? "warning" : "info");

        if (slowest >= 3000)
        {
            yield return new TriageFinding("Slow request", $"The request took {slowest} ms to answer.", "warning");
        }
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
