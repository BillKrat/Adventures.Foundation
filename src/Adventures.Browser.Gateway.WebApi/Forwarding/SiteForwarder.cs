using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Adventures.Browser.Gateway.WebApi;

/// <summary>A site's answer, reduced to what the gateway passes on: a status, and the expected content when it is 200.</summary>
public readonly record struct SiteAnswer<T>(HttpStatusCode Status, T? Value)
    where T : class;

/// <summary>
/// Calls a configured site. Only the request the gateway was written for goes out, with nothing of the visitor's but the name and password of a sign-in
/// or the bearer token of a read; only the expected shape comes back. Nothing of a request or answer is logged.
/// </summary>
public sealed class SiteForwarder(IHttpClientFactory clients, ILogger<SiteForwarder> logger)
{
    public const string ClientName = "browser-gateway";

    private const long MaxAnswerBytes = 4 * 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>The connection handler: a redirect is never followed (it could carry a password elsewhere) and no cookie is kept between visitors.</summary>
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    public Task<SiteAnswer<T>> GetAsync<T>(Uri api, string path, AuthenticationHeaderValue authorization, CancellationToken cancellationToken)
        where T : class
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(api, path));
        request.Headers.Authorization = authorization;
        return SendAsync<T>(request, cancellationToken);
    }

    public Task<SiteAnswer<TAnswer>> PostAsync<TBody, TAnswer>(Uri api, string path, TBody body, CancellationToken cancellationToken)
        where TAnswer : class =>
        SendAsync<TAnswer>(new HttpRequestMessage(HttpMethod.Post, new Uri(api, path)) { Content = JsonContent.Create(body) }, cancellationToken);

    private async Task<SiteAnswer<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        where T : class
    {
        using (request)
        {
            var host = request.RequestUri!.Host;
            try
            {
                var client = clients.CreateClient(ClientName);
                client.Timeout = Timeout;
                client.MaxResponseContentBufferSize = MaxAnswerBytes;
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new(response.StatusCode, null);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
                return value is null ? new(HttpStatusCode.BadGateway, null) : new(HttpStatusCode.OK, value);
            }
            catch (Exception problem) when (problem is HttpRequestException or JsonException or NotSupportedException
                || (problem is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning(problem, "Browser gateway: the site {Host} could not be read.", host);
                return new(HttpStatusCode.BadGateway, null);
            }
        }
    }
}
