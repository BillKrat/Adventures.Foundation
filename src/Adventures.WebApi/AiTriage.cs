using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Adventures.Common.Diagnostics;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Adventures.WebApi;

/// <summary>Configuration section "Anthropic". No key means the AI triage is off (the rule-based triage still works).</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>The API key: a secret (user-secrets on a developer machine, an environment secret on the host), never in a file.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5-5";

    public int MaxOutputTokens { get; set; } = 1200;

    /// <summary>Most AI calls per hour across the app; a guard against a runaway cost.</summary>
    public int MaxCallsPerHour { get; set; } = 20;

    /// <summary>
    /// The day the key expires (not a secret, so it lives in appsettings.json). The health check warns from 14 days
    /// before, so a key that expires does not fail silently; update it together with the key itself.
    /// </summary>
    public DateOnly? KeyExpiresOn { get; set; }
}

/// <summary>The AI's assessment of one request, or the reason there is none. Never carries the key or the report that was sent.</summary>
public sealed record AiTriageResult(
    bool Available,
    string? Reason,
    string? Model,
    string? Summary,
    string? LikelyCause,
    string? Confidence,
    IReadOnlyList<string> NextSteps,
    IReadOnlyList<string> MissingEvidence,
    int InputTokens,
    int OutputTokens,
    int SentCharacters,
    bool FromCache);

public interface IAiTriage
{
    Task<AiTriageResult> AskAsync(
        CorrelationReport report, IReadOnlyList<string> logLines, TriageResult ruleResult, CancellationToken cancellationToken);
}

/// <summary>
/// Asks Claude (Anthropic Messages API) to assess one request from a redacted report: the rule-based triage, the
/// exception and its stack, the kind of failure and its history, and the request's log lines. Explicitly requested by
/// an administrator (a button), read-only, cached for an hour per request, capped per hour in all, and every call is
/// logged (id, model, token counts; never the report). Without an API key it answers "not set up".
/// </summary>
public sealed class AiTriage(
    IHttpClientFactory httpClientFactory,
    IOptions<AnthropicOptions> options,
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<AiTriage> logger) : IAiTriage
{
    public const string ClientName = "Anthropic";
    private const int MaxReportCharacters = 24_000;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private const string SystemPrompt =
        "You are a careful senior .NET and operations engineer triaging ONE production exception of a small ASP.NET Core + Angular app " +
        "(a Web API, a separate MCP server site, a Postgres-backed store; hosted on IIS via SmarterASP.NET, deployed by GitHub Actions over FTP). " +
        "You receive a redacted report: a rule-based triage, the exception and stack, the history of that kind of failure, and the request's log lines. " +
        "Use ONLY that evidence. Do not invent host names, versions, log lines or causes the evidence does not support; if the evidence is not enough, say what is missing. " +
        "Prefer the simplest explanation that fits all the evidence, and say what would confirm it. " +
        "Reply with ONLY a JSON object, no prose around it: " +
        "{\"summary\": string (at most 60 words), \"likelyCause\": string, \"confidence\": \"low\"|\"medium\"|\"high\", " +
        "\"nextSteps\": [string, at most 5, concrete and ordered], \"missingEvidence\": [string]}.";

    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, AiTriageResult Result)> Cache = new();
    private static readonly object Gate = new();
    private static readonly Queue<DateTimeOffset> Calls = new();

    public async Task<AiTriageResult> AskAsync(
        CorrelationReport report, IReadOnlyList<string> logLines, TriageResult ruleResult, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return Unavailable("AI triage is not set up: no Anthropic API key is configured for this environment.", settings.Model);
        }

        if (Cache.TryGetValue(report.CorrelationId, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheLifetime)
        {
            return cached.Result with { FromCache = true };
        }

        if (!TryReserveCall(settings.MaxCallsPerHour))
        {
            return Unavailable($"The hourly limit of {settings.MaxCallsPerHour} AI triage calls has been reached; try again later.", settings.Model);
        }

        var text = await BuildReportAsync(report, logLines, ruleResult, cancellationToken);
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
            request.Headers.Add("x-api-key", settings.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = JsonContent.Create(new
            {
                model = settings.Model,
                max_tokens = settings.MaxOutputTokens,
                system = SystemPrompt,
                messages = new[] { new { role = "user", content = text } },
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (kind, reason) = Explain((int)response.StatusCode, body);
                logger.LogWarning("AI triage for {CorrelationId} failed: HTTP {Status} ({Kind})", report.CorrelationId, (int)response.StatusCode, kind);
                await RecordFailureAsync(new HttpRequestException($"The Anthropic API answered {(int)response.StatusCode} ({kind})"), report.CorrelationId, cancellationToken);
                return Unavailable(reason, settings.Model, text.Length);
            }

            var result = Parse(body, settings.Model, text.Length);
            logger.LogInformation(
                "AI triage for {CorrelationId} with {Model}: {InputTokens} input and {OutputTokens} output tokens, {SentCharacters} characters sent",
                report.CorrelationId, settings.Model, result.InputTokens, result.OutputTokens, text.Length);
            if (result.Available)
            {
                Cache[report.CorrelationId] = (DateTimeOffset.UtcNow, result);
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "AI triage for {CorrelationId} could not reach the AI service", report.CorrelationId);
            await RecordFailureAsync(ex, report.CorrelationId, CancellationToken.None);
            return Unavailable("The AI service could not be reached.", settings.Model, text.Length);
        }
    }

    /// <summary>
    /// What an error from the AI service means for the person holding the button, in plain words. The service's own error
    /// type is kept (it never contains our key); a rejected key says how to replace it, a credit problem says to add funds.
    /// </summary>
    public static (string Kind, string Reason) Explain(int status, string body)
    {
        string kind = "unknown", message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                kind = error.TryGetProperty("type", out var t) ? t.GetString() ?? kind : kind;
                message = error.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
            }
        }
        catch (JsonException)
        {
            // not JSON: the status code alone has to do
        }

        var detail = message.Length == 0 ? string.Empty : $" The service says: {Redactor.ScrubAndLimit(message, 200)}";
        var reason = status switch
        {
            401 => "The Anthropic API key was rejected (expired, revoked or mistyped). Create a new key, store it (vault set, then sync), update the host secret Anthropic__ApiKey and the date Anthropic:KeyExpiresOn, then restart the api site.",
            403 => "The Anthropic API key is not allowed to do this (its workspace or permissions). Check the key's scope in the Anthropic console.",
            429 => "The Anthropic API refused the call: a rate limit or the monthly spend limit was reached. Wait a little, or raise the limit in the Anthropic console.",
            400 when message.Contains("credit", StringComparison.OrdinalIgnoreCase) => "The Anthropic account has no credit left. Add funds in the Anthropic console.",
            >= 500 => "The Anthropic service is having trouble (overloaded or down). Try again in a few minutes.",
            _ => $"The AI service answered HTTP {status}.",
        };
        return (kind, reason + detail);
    }

    /// <summary>An AI failure is an exception like any other: it reaches the exception log (and the admin icon), once an hour per kind.</summary>
    private async Task RecordFailureAsync(Exception failure, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var log = scope.ServiceProvider.GetService<IExceptionLogBll>();
            if (log is not null)
            {
                await log.RecordThrottledAsync(failure, "ai triage", null, correlationId, TimeSpan.FromHours(1), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not record the AI triage failure in the exception log");
        }
    }

    /// <summary>The redacted report: only what triage needs, bounded in size. Public for tests (what leaves the system is what this returns).</summary>
    public async Task<string> BuildReportAsync(
        CorrelationReport report, IReadOnlyList<string> logLines, TriageResult ruleResult, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Correlation id: {report.CorrelationId}");
        builder.AppendLine(environment.IsProduction()
            ? "Environment: Production (the live checks ran from the production API)."
            : $"Environment: {environment.EnvironmentName} - a developer machine: its live checks run with development settings, so a 401 from mcp there (different Jwt signing key) is expected and says nothing about production, and the log lines are the developer machine's own.");
        builder.AppendLine($"Rule-based triage verdict: {ruleResult.Verdict} - {Redactor.ScrubAndLimit(ruleResult.Summary, 300)}");
        foreach (var finding in ruleResult.Findings)
        {
            builder.AppendLine($"- [{finding.Severity}] {finding.Title}: {Redactor.ScrubAndLimit(finding.Detail, 600)}");
        }

        builder.AppendLine();
        foreach (var occurrence in report.Exceptions)
        {
            builder.AppendLine($"Exception: {occurrence.ExceptionType} at {occurrence.OccurredAt:u}");
            builder.AppendLine($"Message: {Redactor.ScrubAndLimit(occurrence.Message, 800)}");
            builder.AppendLine($"Request: {Redactor.ScrubAndLimit(occurrence.Request, 200)} (a signed-in user was involved: {(occurrence.UserName is null ? "no" : "yes")})");
            var stack = await TryGetStackAsync(occurrence.Id, cancellationToken);
            if (stack is not null)
            {
                builder.AppendLine("Stack trace:");
                builder.AppendLine(Redactor.ScrubAndLimit(stack, 5_000));
            }
        }

        foreach (var kind in report.Signatures)
        {
            builder.AppendLine($"Kind of failure: seen {kind.OccurrenceCount} time(s), first {kind.FirstSeen:u}, last {kind.LastSeen:u}, status {kind.Status}, top frame {Redactor.ScrubAndLimit(kind.TopFrame, 200)}");
        }

        builder.AppendLine();
        builder.AppendLine($"Log lines of this request ({logLines.Count}, showing up to 40):");
        foreach (var line in logLines.Take(40))
        {
            builder.AppendLine(Redactor.ScrubAndLimit(Summarise(line), 500));
        }

        var all = builder.ToString();
        return all.Length <= MaxReportCharacters ? all : all[..MaxReportCharacters] + "\n...[report cut]";
    }

    private async Task<string?> TryGetStackAsync(string entryId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var log = scope.ServiceProvider.GetService<IExceptionLogBll>();
            var entry = log is null ? null : await log.GetAsync(entryId, cancellationToken);
            return entry?.GetValue("StackTrace") as string;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read the stack trace of {EntryId} for the AI report", entryId);
            return null;
        }
    }

    /// <summary>One compact log line as "time level message (exception first line)"; anything unparsable is passed on as text.</summary>
    private static string Summarise(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            string Get(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
            var exception = Get("@x");
            var firstLine = exception.Split('\n', 2)[0];
            return $"{Get("@t")} {(Get("@l") is { Length: > 0 } level ? level : "Information")} {Get("@m")}{(firstLine.Length > 0 ? " | " + firstLine : string.Empty)}";
        }
        catch (JsonException)
        {
            return line;
        }
    }

    private static AiTriageResult Parse(string body, string model, int sentCharacters)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var inputTokens = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0;
        var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
        var text = root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text").Select(c => c.GetProperty("text").GetString()))
            : string.Empty;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return Unavailable("The AI answered, but not in the expected form.", model, sentCharacters, inputTokens, outputTokens);
        }

        try
        {
            var answer = JsonSerializer.Deserialize<AiAnswer>(text[start..(end + 1)], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new AiTriageResult(
                true, null, model, answer?.Summary, answer?.LikelyCause, answer?.Confidence,
                answer?.NextSteps ?? [], answer?.MissingEvidence ?? [], inputTokens, outputTokens, sentCharacters, false);
        }
        catch (JsonException)
        {
            return Unavailable("The AI answered, but not in the expected form.", model, sentCharacters, inputTokens, outputTokens);
        }
    }

    private static AiTriageResult Unavailable(string reason, string model, int sent = 0, int input = 0, int output = 0) =>
        new(false, reason, model, null, null, null, [], [], input, output, sent, false);

    private static bool TryReserveCall(int maxPerHour)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            while (Calls.Count > 0 && now - Calls.Peek() > TimeSpan.FromHours(1))
            {
                Calls.Dequeue();
            }

            if (Calls.Count >= maxPerHour)
            {
                return false;
            }

            Calls.Enqueue(now);
            return true;
        }
    }

    /// <summary>For tests: forget cached answers and the hourly count.</summary>
    public static void ResetForTests()
    {
        Cache.Clear();
        lock (Gate)
        {
            Calls.Clear();
        }
    }

    private sealed record AiAnswer(
        string? Summary,
        string? LikelyCause,
        string? Confidence,
        [property: JsonPropertyName("nextSteps")] List<string>? NextSteps,
        [property: JsonPropertyName("missingEvidence")] List<string>? MissingEvidence);
}
