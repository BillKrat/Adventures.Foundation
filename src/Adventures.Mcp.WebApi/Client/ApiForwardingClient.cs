using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Adventures.Mcp.WebApi;

/// <summary>The API's answer to a forwarded question: its status and body, untouched, or a 502 when it could not be reached.</summary>
public sealed record ApiAnswer(int StatusCode, string Body, bool Unreachable = false);

/// <summary>
/// Forwards a question or an authoring call to the API (which owns the exception store, the log files and the content) with the
/// caller's own token (both sites validate the same issuer and signing key). Shared by the REST controller and the MCP wire-protocol
/// endpoint so both pass the answer through untouched and the API stays the single place that decides what is allowed and shown.
/// </summary>
public sealed class ApiForwardingClient(IHttpClientFactory httpClientFactory, ILogger<ApiForwardingClient> logger)
{
    public const string ApiClientName = "Api";

    public Task<ApiAnswer> GetAsync(string path, string? authorization, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, authorization, cancellationToken);

    /// <summary>Sends any method with an optional JSON body; the answer (including a refusal such as 409) is returned as the API gave it.</summary>
    public async Task<ApiAnswer> SendAsync(HttpMethod method, string path, JsonNode? body, string? authorization, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(ApiClientName);
            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrEmpty(authorization))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            return new ApiAnswer((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The API being unreachable is itself something to see: say so with the id that finds it in this site's logs.
            logger.LogError(ex, "The MCP server could not reach the API for {Method} {Path}", method, path);
            return new ApiAnswer(StatusCodes.Status502BadGateway, string.Empty, Unreachable: true);
        }
    }
}
