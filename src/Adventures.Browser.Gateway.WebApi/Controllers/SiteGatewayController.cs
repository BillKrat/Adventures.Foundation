using System.Net;
using System.Net.Http.Headers;
using Adventures.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Adventures.Browser.Gateway.WebApi;

/// <summary>
/// The browser applications' one API address. Sign-in and read-only schema requests for a configured domain go to that domain's site, which alone decides
/// who the person is and what they may see. A domain that is not configured answers exactly like a refused sign-in, and no route lists the domains.
/// </summary>
[ApiController]
[AllowAnonymous] // the gateway has no users of its own: the site checks the password and the token
[Route("api/sites/{domain}")]
public sealed class SiteGatewayController(SiteDirectory sites, SiteForwarder forwarder) : ControllerBase
{
    private const int MaxCredentialLength = 256;
    private const int MaxTokenLength = 8192;

    [HttpPost("token")]
    [EnableRateLimiting(BrowserGatewayExtensions.SignInPolicy)]
    public async Task<ActionResult<SiteToken>> Token(string domain, [FromBody] SiteSignIn request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (request.UserName is not { Length: > 0 and <= MaxCredentialLength } || request.Password is not { Length: > 0 and <= MaxCredentialLength })
        {
            return Problem(title: "A user name and a password are needed.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (sites.Find(domain) is not { } api)
        {
            return Refused(HttpStatusCode.Unauthorized);
        }

        var answer = await forwarder.PostAsync<SiteSignIn, SiteToken>(api, "api/auth/token", request, cancellationToken);
        return answer.Value is { AccessToken.Length: > 0 } token ? Ok(token) : Refused(answer.Status == HttpStatusCode.OK ? HttpStatusCode.BadGateway : answer.Status);
    }

    [HttpGet("context")]
    [EnableRateLimiting(BrowserGatewayExtensions.ReadPolicy)]
    public Task<ActionResult<SchemaBrowserContext>> Context(string domain, CancellationToken cancellationToken) =>
        ReadAsync<SchemaBrowserContext>(domain, "api/browser/context", cancellationToken);

    [HttpGet("schemas")]
    [EnableRateLimiting(BrowserGatewayExtensions.ReadPolicy)]
    public Task<ActionResult<SchemaCatalogList>> Schemas(string domain, CancellationToken cancellationToken) =>
        ReadAsync<SchemaCatalogList>(domain, "api/browser/schemas", cancellationToken);

    [HttpGet("schema")]
    [EnableRateLimiting(BrowserGatewayExtensions.ReadPolicy)]
    public Task<ActionResult<SchemaDetail>> Schema(string domain, [FromQuery] string id, CancellationToken cancellationToken) =>
        ReadAsync<SchemaDetail>(domain, "api/browser/schema?id=" + Uri.EscapeDataString(id), cancellationToken);

    private async Task<ActionResult<T>> ReadAsync<T>(string domain, string path, CancellationToken cancellationToken)
        where T : class
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Headers.Authorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || authorization.Parameter is not { Length: > 0 and <= MaxTokenLength }
            || sites.Find(domain) is not { } api)
        {
            return Refused(HttpStatusCode.Unauthorized);
        }

        var answer = await forwarder.GetAsync<T>(api, path, authorization, cancellationToken);
        return answer.Value is { } value ? Ok(value) : Refused(answer.Status);
    }

    // The gateway's own words for each refusal: nothing a site wrote in an error reaches the visitor.
    private ObjectResult Refused(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => Problem(title: "Sign-in was refused or has ended.", statusCode: 401),
        HttpStatusCode.Forbidden => Problem(title: "This account may not browse this site's schemas (it needs the schema.read right), or must change its password on the site first.", statusCode: 403),
        HttpStatusCode.NotFound => Problem(title: "Not found. The site may not have the schema browser switched on.", statusCode: 404),
        HttpStatusCode.TooManyRequests => Problem(title: "Too many requests. Wait a minute and try again.", statusCode: 429),
        _ => Problem(title: "The site did not answer as expected.", statusCode: 502),
    };
}
