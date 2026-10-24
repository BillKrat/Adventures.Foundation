// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Text.Json;

namespace Adventures.Common.Diagnostics;

/// <summary>
/// Finds the log lines of one request in the compact-JSON log files (<c>webapi-json-*.log</c>; the pattern also accepts the
/// earlier <c>webapi-.json*.log</c> spelling), oldest first, so an administrator or an MCP tool can read what a request did
/// from its CorrelationId alone. Read-only; the id must be well formed (it is matched as text, never interpreted).
/// Exceptions are rare and their full text is what triage needs, so a line is returned whole up to
/// <see cref="MaxLineLength"/>; a longer one is replaced by a small valid JSON stub that says how long it was (never cut
/// in the middle, which would break the JSON for a machine reader). The whole answer is bounded by <see cref="MaxTotalLength"/>.
/// </summary>
public static class LogSearch
{
    public const int MaxLineLength = 250_000;
    public const int MaxTotalLength = 4_000_000;

    public static async Task<IReadOnlyList<string>> FindLinesAsync(
        string logDirectory, string correlationId, int max = 200, CancellationToken cancellationToken = default)
    {
        if (!CorrelationIds.IsWellFormed(correlationId) || !Directory.Exists(logDirectory))
        {
            return [];
        }

        var needle = $"\"{CorrelationIds.LogPropertyName}\":\"{correlationId}\"";
        var found = new List<string>();
        foreach (var file in new DirectoryInfo(logDirectory).GetFiles("webapi-*json*.log").OrderByDescending(f => f.Name))
        {
            // Serilog keeps today's file open for writing, so share it.
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var inFile = new List<string>();
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Contains(needle, StringComparison.Ordinal))
                {
                    inFile.Add(line.Length <= MaxLineLength ? line : Stub(line));
                }
            }

            found.InsertRange(0, inFile);
            if (found.Count >= max)
            {
                break;
            }
        }

        var newest = found.Count <= max ? found : found.GetRange(found.Count - max, max);
        return WithinBudget(newest);
    }

    /// <summary>Keeps the newest lines that fit the total budget (a request's last words matter most).</summary>
    private static IReadOnlyList<string> WithinBudget(List<string> lines)
    {
        var total = 0;
        var keep = 0;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            total += lines[i].Length;
            if (total > MaxTotalLength)
            {
                break;
            }

            keep++;
        }

        return keep == lines.Count ? lines : lines.GetRange(lines.Count - keep, keep);
    }

    /// <summary>A valid JSON replacement for an over-long line: its time, level and message (trimmed) and how long it was.</summary>
    private static string Stub(string line)
    {
        string? time = null, level = null, message = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            time = document.RootElement.TryGetProperty("@t", out var t) ? t.GetString() : null;
            level = document.RootElement.TryGetProperty("@l", out var l) ? l.GetString() : null;
            message = document.RootElement.TryGetProperty("@m", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            // an unparsable giant line still gets a stub with its length
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@t"] = time,
            ["@l"] = level,
            ["@m"] = message is { Length: > 1_000 } ? message[..1_000] : message,
            ["truncated"] = true,
            ["originalLength"] = line.Length,
        });
    }
}
