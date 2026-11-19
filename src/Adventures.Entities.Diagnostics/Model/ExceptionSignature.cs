// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Adventures.Entities;

/// <summary>
/// What makes two exceptions "the same failure": the outer exception type, the root-cause type, the message with its
/// volatile parts (ids, numbers, quoted values) replaced by placeholders, and the first application stack frame
/// (type and method, never a line number, so a recompile does not split a signature). The hash of those parts is the
/// signature's identity; a repeat of the same failure reuses it instead of creating a new one.
/// </summary>
public sealed partial record ExceptionSignature(string Hash, string ExceptionType, string MessagePattern, string TopFrame)
{
    private const int MaxPatternLength = 500;

    public static ExceptionSignature From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var root = exception;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var type = exception.GetType().FullName ?? exception.GetType().Name;
        var rootType = root.GetType().FullName ?? root.GetType().Name;
        var pattern = NormalizeMessage(exception.Message);
        var topFrame = TopApplicationFrame(root) ?? TopApplicationFrame(exception) ?? "(no stack)";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}|{rootType}|{pattern}|{topFrame}")))[..32]
            .ToLowerInvariant();
        return new ExceptionSignature(hash, type, pattern, topFrame);
    }

    /// <summary>Replaces GUIDs, long hex runs, quoted values and numbers with placeholders so messages that differ only by value group together.</summary>
    public static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var text = GuidPattern().Replace(message, "{guid}");
        text = HexPattern().Replace(text, "{hex}");
        text = QuotedPattern().Replace(text, "'{value}'");
        text = NumberPattern().Replace(text, "{n}");
        text = WhitespacePattern().Replace(text, " ").Trim();
        return text.Length <= MaxPatternLength ? text : text[..MaxPatternLength];
    }

    /// <summary>First frame whose type is not System.* or Microsoft.*, as "Namespace.Type.Method"; null when there is none.</summary>
    private static string? TopApplicationFrame(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames())
        {
            var method = frame.GetMethod();
            var declaring = method?.DeclaringType;
            if (method is null || declaring is null)
            {
                continue;
            }

            var ns = declaring.Namespace ?? string.Empty;
            if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
                || ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal))
            {
                continue;
            }

            // Compiler-generated async/lambda types (Outer+<Name>d__3) are reported as their owning type and method.
            var typeName = declaring.FullName ?? declaring.Name;
            var methodName = method.Name;
            var plus = typeName.IndexOf('+', StringComparison.Ordinal);
            if (plus > 0 && typeName.AsSpan(plus + 1).StartsWith("<", StringComparison.Ordinal))
            {
                var inner = typeName[(plus + 1)..];
                methodName = inner.Length > 1 && inner.IndexOf('>', StringComparison.Ordinal) is var end and > 1 ? inner[1..end] : methodName;
                typeName = typeName[..plus];
            }

            return $"{typeName}.{methodName}";
        }

        return null;
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{12,}\b")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"'[^']*'|""[^""]*""")]
    private static partial Regex QuotedPattern();

    [GeneratedRegex(@"\b\d+(\.\d+)?\b")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
