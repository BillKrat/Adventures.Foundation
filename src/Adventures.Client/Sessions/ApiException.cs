using System.Net;

namespace Adventures.Client;

/// <summary>
/// A refused or failed call to the API, reduced to what a person may be shown: the status, the server's one-line title and the per-field messages
/// of a validation refusal. Never the response body, a stack trace or a token (AppArch exception shielding; R18).
/// </summary>
public sealed class ApiException(HttpStatusCode statusCode, string message, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Field name to messages for a 400 validation refusal; empty otherwise.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>(errors ?? new Dictionary<string, string[]>(), StringComparer.OrdinalIgnoreCase);

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
}
