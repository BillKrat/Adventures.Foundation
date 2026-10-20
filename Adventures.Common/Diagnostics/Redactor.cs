using System.Text.RegularExpressions;

namespace Adventures.Common.Diagnostics;

/// <summary>
/// Scrubs text before it leaves the system for a third party (the AI triage). It removes what must never be sent -
/// tokens, keys, passwords, connection-string parts, e-mail addresses, IP addresses and the user name inside a Windows
/// path - and keeps what triage needs: exception types and messages, our own host names, request paths, times and the
/// correlation id. It is a safety net with known limits (it cannot recognise a secret that looks like prose), so the
/// report builder also decides what is included at all.
/// </summary>
public static partial class Redactor
{
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = Jwt().Replace(text, "[jwt]");
        result = ApiKey().Replace(result, "[api-key]");
        result = Bearer().Replace(result, "Bearer [token]");
        result = ConnectionPart().Replace(result, "$1=[redacted]");
        result = KeyValueSecret().Replace(result, "$1=[redacted]");
        result = Email().Replace(result, "[email]");
        result = IpAddress().Replace(result, "[ip]");
        result = WindowsUser().Replace(result, "$1[user]");
        return result;
    }

    /// <summary>Scrubs and bounds the length, marking a cut.</summary>
    public static string ScrubAndLimit(string? text, int max)
    {
        var scrubbed = Scrub(text);
        return scrubbed.Length <= max ? scrubbed : scrubbed[..max] + " ...[cut]";
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{4,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"sk-[A-Za-z0-9]+-[A-Za-z0-9_-]{8,}")]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._~+/=-]{8,}")]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i)\b(Host|Server|Data Source|User ?Id|Username|Password|Pwd)\s*=\s*[^;\r\n]+")]
    private static partial Regex ConnectionPart();

    [GeneratedRegex(@"(?i)\b(api[_-]?key|secret|token|password|passwd|pwd)\s*[=:]\s*[^\s;,""']+")]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex IpAddress();

    [GeneratedRegex(@"(?i)([A-Z]:\\Users\\)[^\\\s]+")]
    private static partial Regex WindowsUser();
}
