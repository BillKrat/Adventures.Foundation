using System.Text.Json;

namespace Adventures.Client;

/// <summary>Turns a failed response into an <see cref="ApiException"/> carrying only the safe parts of a ProblemDetails body.</summary>
internal static class ApiErrors
{
    public static async Task<ApiException> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? title = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                {
                    errors = e.EnumerateObject()
                        .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                        .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString()!).ToArray());
                }
            }
        }
        catch (JsonException)
        {
            // not a problem document: fall back to the generic line below
        }

        return new ApiException(response.StatusCode, string.IsNullOrWhiteSpace(title) ? $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}." : title, errors);
    }
}
