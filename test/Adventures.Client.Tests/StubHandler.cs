using System.Net;
using System.Text;
using System.Text.Json;

namespace Adventures.Client.Tests;

/// <summary>Answers every request from a function and records what was asked, so tests see the URL, headers and body the client sent.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string? Authorization, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body));
        return respond(request);
    }

    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(string body, HttpStatusCode status) => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);

    public static HttpClient Client(StubHandler handler) => new(handler) { BaseAddress = new Uri("https://api.test/") };
}
