// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Adventures.Common.Diagnostics;
using Adventures.Security;
using Microsoft.Extensions.Logging;

namespace Adventures.WebApi;

/// <summary>
/// The real live checks, used by the health endpoint and the triage alike: mcp's <c>api/hello</c> over a token this app
/// mints for itself, and a check of the data store (the database). Each probe fails soft - an exception becomes a
/// result with a short code, never a thrown error - so a dependency that is down cannot take the caller down with it.
/// </summary>
public sealed class DependencyProbes(
    IJwtTokenService tokenService,
    IHttpClientFactory httpClientFactory,
    IStoreProbe? store = null,
    ILogger? logger = null) : IDependencyProbes
{
    /// <summary>How long the database check may take before it counts as failed.</summary>
    public static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(5);

    public async Task<ProbeResult> ProbeMcpAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var token = tokenService.IssueClientToken("mcp-host", ["mcp.hello"]).AccessToken;
            using var client = httpClientFactory.CreateClient("McpServer");
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/hello");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger?.LogWarning(
                    "mcp /api/hello returned {StatusCode} {ReasonPhrase}. Body: {Body}", (int)response.StatusCode, response.ReasonPhrase, body);
                return new ProbeResult(
                    false, watch.ElapsedMilliseconds, $"http_{(int)response.StatusCode}",
                    Failure: new HttpRequestException($"mcp /api/hello returned {(int)response.StatusCode} {response.ReasonPhrase}"));
            }

            var parsed = await response.Content.ReadFromJsonAsync<McpHello>(cancellationToken);
            return new ProbeResult(true, watch.ElapsedMilliseconds, "ok", parsed?.Message);
        }
        catch (Exception ex)
        {
            // Broad on purpose: a downstream probe must never turn its caller into a 500.
            logger?.LogWarning(ex, "Failed to reach mcp for the probe.");
            return Failed(ex, watch);
        }
    }

    public async Task<ProbeResult> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return new ProbeResult(true, 0, "not_configured", Skipped: true);
        }

        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DatabaseTimeout);
            await store.CheckAsync(timeout.Token);
            return new ProbeResult(true, watch.ElapsedMilliseconds, "ok");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "The probe could not reach the database.");
            return Failed(ex, watch);
        }
    }

    private static ProbeResult Failed(Exception ex, Stopwatch watch) =>
        new(false, watch.ElapsedMilliseconds, ex is OperationCanceledException ? "timeout" : $"exception_{ex.GetType().Name}", Failure: ex);

    private sealed record McpHello(string Message);
}
