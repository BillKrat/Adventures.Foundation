using System.Text.Json;

namespace Adventures.WebApi;

/// <summary>One failure written to the journal.</summary>
public sealed record JournalEntry(DateTimeOffset At, string Source, string Type, string Message, string? Stack);

/// <summary>
/// A plain file (<c>failures.jsonl</c>, one JSON object per line) where a failure is written the moment it happens, with no
/// dependency on the database or the DI container, so it works when startup itself is failing or the process is dying. On
/// the next healthy start <see cref="FailureReporter"/> moves the entries into the exception log, where an administrator
/// sees them, and then marks them reported. Until then the file (and the text log) is what an operator reads.
/// </summary>
public static class FailureJournal
{
    public const string FileName = "failures.jsonl";
    private const int MaxStack = 20_000;
    private static readonly object Gate = new();

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>Appends the failure. Never throws: writing the journal must not make a failure worse.</summary>
    public static void Write(string directory, string source, Exception exception)
    {
        try
        {
            var entry = new JournalEntry(
                DateTimeOffset.UtcNow, source, exception.GetType().FullName ?? exception.GetType().Name, exception.Message,
                Limit(exception.ToString()));
            Directory.CreateDirectory(directory);
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // nothing more can be done; the text log still has the failure
        }
    }

    public static IReadOnlyList<JournalEntry> ReadPending(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<JournalEntry>();
        lock (Gate)
        {
            foreach (var line in File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                try
                {
                    if (JsonSerializer.Deserialize<JournalEntry>(line) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // a half-written line (the process died mid-write) is skipped, the rest is still reported
                }
            }
        }

        return entries;
    }

    /// <summary>Marks the pending entries reported by moving the file aside (kept, so nothing is lost).</summary>
    public static void MarkReported(string directory)
    {
        var path = Path.Combine(directory, FileName);
        lock (Gate)
        {
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(directory, $"failures-reported-{DateTime.UtcNow:yyyyMMddHHmmssfff}.jsonl"));
            }
        }
    }

    private static string Limit(string text) => text.Length <= MaxStack ? text : text[..MaxStack] + " ...[cut]";
}
