using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Adventures.Client;
using Adventures.Entities;

namespace Adventures.Browser.Client;

/// <summary>
/// <see cref="IBrowserApi"/> over HTTP. Each domain's token is kept in memory only, never written or logged, and is attached to a request for that domain
/// alone (never as a default header), so one site's token cannot travel with a request for another.
/// </summary>
public sealed class BrowserApi(HttpClient http) : IBrowserApi
{
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSignedIn(string domain) => _tokens.ContainsKey(domain);

    public async Task<SiteSignInResult> SignInAsync(string domain, string userName, string password, CancellationToken cancellationToken = default)
    {
        SignOut(domain);
        using var response = await http.PostAsJsonAsync(Route(domain, "token"), new SiteSignIn(userName, password), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return SiteSignInResult.Refused;
        }

        var token = await ReadAsync<SiteToken>(response, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new ApiException(response.StatusCode, "The server did not return a sign-in token.");
        }

        if (token.MustChangePassword)
        {
            return SiteSignInResult.MustChangePassword; // the token would be refused for everything this application does, so it is not kept
        }

        _tokens[domain] = token.AccessToken;
        return SiteSignInResult.SignedIn;
    }

    public void SignOut(string domain) => _tokens.TryRemove(domain, out _);

    public async Task<SchemaBrowserContext> GetContextAsync(string domain, CancellationToken cancellationToken = default) =>
        await GetAsync<SchemaBrowserContext>(domain, "context", false, cancellationToken).ConfigureAwait(false) ?? throw Missing();

    public async Task<SchemaCatalogList> GetSchemasAsync(string domain, CancellationToken cancellationToken = default) =>
        await GetAsync<SchemaCatalogList>(domain, "schemas", false, cancellationToken).ConfigureAwait(false) ?? throw Missing();

    public Task<SchemaDetail?> GetSchemaAsync(string domain, string schemaId, CancellationToken cancellationToken = default) =>
        GetAsync<SchemaDetail>(domain, "schema?id=" + Uri.EscapeDataString(schemaId), true, cancellationToken);

    private static string Route(string domain, string rest) => $"api/sites/{Uri.EscapeDataString(domain)}/{rest}";

    private static ApiException Missing() => new(HttpStatusCode.NotFound, "The site does not offer the schema browser.");

    private async Task<T?> GetAsync<T>(string domain, string rest, bool missingIsNull, CancellationToken cancellationToken)
        where T : class
    {
        if (!_tokens.TryGetValue(domain, out var token))
        {
            throw new ApiException(HttpStatusCode.Unauthorized, "Sign in to this site first.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Route(domain, rest));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokens.TryRemove(new KeyValuePair<string, string>(domain, token)); // lapsed: forget it, unless a newer sign-in replaced it
        }

        if (missingIsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The content of a 200; otherwise an <see cref="ApiException"/> with the browser API's one-line reason, never the response body.</summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
                    ?? throw new ApiException(response.StatusCode, "The server returned nothing.");
            }

            var problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                throw new ApiException(response.StatusCode, problem.Title);
            }
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            // not what was expected: fall back to the plain line below
        }

        throw new ApiException(response.StatusCode, $"The server answered {(int)response.StatusCode}.");
    }

    private sealed record Problem(string? Title);
}
