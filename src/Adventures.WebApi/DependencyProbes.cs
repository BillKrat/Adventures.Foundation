using System.Diagnostics;
using System.Net.Http.Headers;
using Adventures.Common.Diagnostics;
using Adventures.Data.NQuad;
using Adventures.Security;

namespace Adventures.WebApi;

/// <summary>The real live checks for the triage: the same two calls the health endpoint makes (mcp hello over a self-minted token, a database count).</summary>
public sealed class DependencyProbes(IJwtTokenService tokenService, IHttpClientFactory httpClientFactory, INQuadStore store) : IDependencyProbes
{
    public async Task<(bool Ok, long DurationMs, string Detail)> ProbeMcpAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var token = tokenService.IssueClientToken("mcp-host", ["mcp.hello"]).AccessToken;
            using var client = httpClientFactory.CreateClient("McpServer");
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/hello");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, watch.ElapsedMilliseconds, "ok")
                : (false, watch.ElapsedMilliseconds, $"http_{(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, watch.ElapsedMilliseconds, ex is OperationCanceledException ? "timeout" : $"exception_{ex.GetType().Name}");
        }
    }

    public async Task<(bool Ok, long DurationMs, string Detail)> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await store.CountAsync(timeout.Token);
            return (true, watch.ElapsedMilliseconds, "ok");
        }
        catch (Exception ex)
        {
            return (false, watch.ElapsedMilliseconds, ex is OperationCanceledException ? "timeout" : $"exception_{ex.GetType().Name}");
        }
    }
}
