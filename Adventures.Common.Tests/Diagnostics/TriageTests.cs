using Adventures.Common.Diagnostics;
using Xunit;

namespace Adventures.Common.Tests.Diagnostics;

/// <summary>The Diagnose button's triage: rules per kind of exception, live check separating "recovered" from "ongoing", the hand-off command.</summary>
public sealed class TriageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 0, 0, TimeSpan.Zero);

    private sealed class FakeProbes(bool mcpOk, bool dbOk) : IDependencyProbes
    {
        public int McpCalls { get; private set; }

        public int DatabaseCalls { get; private set; }

        public Task<ProbeResult> ProbeMcpAsync(CancellationToken cancellationToken)
        {
            McpCalls++;
            return Task.FromResult<ProbeResult>((mcpOk, 18L, mcpOk ? "ok" : "exception_HttpRequestException"));
        }

        public Task<ProbeResult> ProbeDatabaseAsync(CancellationToken cancellationToken)
        {
            DatabaseCalls++;
            return Task.FromResult<ProbeResult>((dbOk, 3L, dbOk ? "ok" : "timeout"));
        }
    }

    private static CorrelationReport ReportFor(string type, string message, string request, DateTimeOffset? lastSeen = null) =>
        new(
            "req-1",
            [new ExceptionOccurrence("e1", Now.AddMinutes(-90), type, message, request, "Admin", "req-1", "s1")],
            [new ExceptionSignatureSummary("s1", type, message, "Top.Frame", Now.AddHours(-3), lastSeen ?? Now.AddMinutes(-90), 4, "New")]);

    [Theory]
    [InlineData("startup at 2026-10-20 10:00:00Z", "startup failure")]
    [InlineData("background service: Worker", "background or process-level")]
    public async Task APersistedFailure_GetsARuleForStartupAndForBackgroundWork(string request, string expected)
    {
        var report = ReportFor("Adventures.Entities.PersistedFailureException", "System.InvalidOperationException: no key", request);

        var result = await Triage.RunAsync(report, [], new FakeProbes(true, true), Now, CancellationToken.None);

        Assert.Contains(expected, result.Summary);
        Assert.DoesNotContain(result.Findings, f => f.Title == "Rule");
    }

    [Fact]
    public async Task McpProbeException_WhenMcpAnswersNow_IsRecovered()
    {
        var probes = new FakeProbes(mcpOk: true, dbOk: true);
        var report = ReportFor("System.Net.Http.HttpRequestException", "The SSL connection could not be established, see inner exception.", "health probe: mcp");

        var result = await Triage.RunAsync(report, [], probes, Now, CancellationToken.None);

        Assert.Equal("Recovered", result.Verdict);
        Assert.Equal(1, probes.McpCalls);
        Assert.Contains(result.Findings, f => f.Title == "Likely cause" && f.Detail.Contains("TLS handshake"));
        Assert.Contains(result.Findings, f => f.Title == "Live check now" && f.Detail.Contains("outage is over"));
        Assert.Contains(result.Findings, f => f.Title == "History of this kind of failure" && f.Detail.Contains("Seen 4 time(s)"));
    }

    [Fact]
    public async Task McpProbeException_WhenMcpStillFails_IsOngoingAndSaysWhatToCheck()
    {
        var probes = new FakeProbes(mcpOk: false, dbOk: true);
        var report = ReportFor("System.Threading.Tasks.TaskCanceledException", "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.", "health probe: mcp");

        var result = await Triage.RunAsync(report, [], probes, Now, CancellationToken.None);

        Assert.Equal("Ongoing", result.Verdict);
        Assert.Contains(result.Findings, f => f.Severity == "problem" && f.Detail.Contains("still fails"));
        Assert.Contains(result.NextSteps, s => s.Contains("certificate"));
    }

    [Fact]
    public async Task McpAnswering401_IsExplainedAsAKeyMismatch()
    {
        var report = ReportFor("System.Net.Http.HttpRequestException", "mcp /api/hello returned 401 Unauthorized", "health probe: mcp");

        var result = await Triage.RunAsync(report, [], new Http401Probes(), Now, CancellationToken.None);

        Assert.Contains(result.Findings, f => f.Title == "Live check now" && f.Detail.Contains("signing keys differ"));
        Assert.Equal("Ongoing", result.Verdict);
    }

    private sealed class Http401Probes : IDependencyProbes
    {
        public Task<ProbeResult> ProbeMcpAsync(CancellationToken cancellationToken) => Task.FromResult<ProbeResult>((false, 20L, "http_401"));

        public Task<ProbeResult> ProbeDatabaseAsync(CancellationToken cancellationToken) => Task.FromResult<ProbeResult>((true, 1L, "ok"));
    }

    [Fact]
    public async Task DatabaseProbeException_UsesTheDatabaseCheck()
    {
        var probes = new FakeProbes(mcpOk: true, dbOk: false);
        var report = ReportFor("System.InvalidOperationException", "db down", "health probe: database");

        var result = await Triage.RunAsync(report, [], probes, Now, CancellationToken.None);

        Assert.Equal("Ongoing", result.Verdict);
        Assert.Equal(1, probes.DatabaseCalls);
        Assert.Equal(0, probes.McpCalls);
    }

    [Fact]
    public async Task ClientReportedException_IsABrowserSideError()
    {
        var report = ReportFor("Adventures.Entities.ClientReportedException", "TypeError: x is undefined", "client: /profile");

        var result = await Triage.RunAsync(report, [], new FakeProbes(true, true), Now, CancellationToken.None);

        Assert.Equal("A browser-side error.", result.Summary);
        Assert.Contains(result.NextSteps, s => s.Contains("Reproduce"));
    }

    [Fact]
    public async Task AnExceptionNoRuleKnows_StillGetsFactsAndTheHandOff()
    {
        var report = ReportFor("System.InvalidOperationException", "something odd", "GET /api/profile/me");

        var result = await Triage.RunAsync(report, [], new FakeProbes(true, true), Now, CancellationToken.None);

        Assert.Equal("Needs attention", result.Verdict);
        Assert.Contains(result.Findings, f => f.Title == "What happened" && f.Detail.Contains("GET /api/profile/me"));
        Assert.Contains(result.NextSteps, s => s.Contains("hand it to Claude"));
        Assert.Contains("-CorrelationId\",\"req-1\"", result.HandOffCommand);
        Assert.Contains("Claude-diagnose.ps1", result.HandOffCommand);
    }

    [Fact]
    public async Task RecentRepeat_IsFlaggedAsAWarning()
    {
        var report = ReportFor("System.InvalidOperationException", "again", "GET /x", lastSeen: Now.AddMinutes(-5));

        var result = await Triage.RunAsync(report, [], new FakeProbes(true, true), Now, CancellationToken.None);

        Assert.Contains(result.Findings, f => f.Title == "History of this kind of failure" && f.Severity == "warning" && f.Detail.Contains("last hour"));
    }

    [Fact]
    public async Task LogLines_AreSummarised_ErrorsWarningsAndSlowRequests()
    {
        var lines = new[]
        {
            """{"@t":"2026-10-19T13:00:00Z","@m":"Sending HTTP request"}""",
            """{"@t":"2026-10-19T13:00:01Z","@l":"Warning","@m":"Failed to reach mcp"}""",
            """{"@t":"2026-10-19T13:00:02Z","@l":"Error","@m":"Execution attempt failed"}""",
            """{"@t":"2026-10-19T13:00:08Z","@m":"HTTP \"GET\" \"/api/health\" responded 200 in 7524.5 ms"}""",
            "not json at all",
        };
        var report = ReportFor("System.InvalidOperationException", "x", "GET /x");

        var result = await Triage.RunAsync(report, lines, new FakeProbes(true, true), Now, CancellationToken.None);

        var log = Assert.Single(result.Findings, f => f.Title == "Log lines of this request");
        Assert.Contains("5 line(s): 1 error(s), 1 warning(s)", log.Detail);
        Assert.Equal("problem", log.Severity);
        Assert.Contains(result.Findings, f => f.Title == "Slow request" && f.Detail.Contains("7524"));
    }

    [Fact]
    public async Task NothingRecorded_SaysSoAndStillOffersTheHandOff()
    {
        var empty = new CorrelationReport("req-9", [], []);

        var result = await Triage.RunAsync(empty, [], new FakeProbes(true, true), Now, CancellationToken.None);

        Assert.Equal("Nothing recorded", result.Verdict);
        Assert.Contains("req-9", result.HandOffCommand);
    }
}
