using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Adventures.Identity.WebApi;

public sealed record ForgotPasswordRequest(string? Identifier, string? ClientBase);

public sealed record ResetPasswordRequest(string? User, string? Token, string? NewPassword);

/// <summary>
/// Forgot my password, anonymous by nature. Asking always answers 204 (the mail, if any, is sent after the response is complete, so neither the answer nor its timing tells whether
/// the person exists). The link base must be a registered host. Opening the link and choosing a password answers one plain failure for every bad link.
/// </summary>
[ApiController]
[Route("api/password-reset")]
[AllowAnonymous]
public class PasswordResetController(
    PasswordResetService reset, ITenantHostSource hosts, IHostEnvironment environment, IConfiguration configuration, Adventures.WebApi.IRequestTenant requestTenant) : ApiControllerBase
{
    private const string InvalidLinkText = "This link is not valid any more. Ask for a new one from the sign-in page.";

    [HttpPost("request")]
    public async Task<IActionResult> Request([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var linkBase = await ClientLinkBase.ResolveAsync(request.ClientBase, hosts, environment, cancellationToken);
        if (linkBase is null)
        {
            return BadRequest("The address of this site is not one we know. Open the site by its own address and try again.");
        }

        var identifier = request.Identifier ?? string.Empty;
        AfterResponse(() => reset.RequestAsync(identifier, linkBase + "/reset-password"));
        return NoContent();
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var tenant = Adventures.WebApi.SiteDomain.Tenant(requestTenant, configuration, "global-webnet.com");
        return await reset.ResetAsync(tenant, request.User ?? string.Empty, request.Token ?? string.Empty, request.NewPassword ?? string.Empty, cancellationToken) switch
        {
            PasswordResetResult.Done => NoContent(),
            PasswordResetResult.WeakPassword => UnprocessableEntity("Choose a longer password: " + PasswordPolicy.Description),
            _ => BadRequest(InvalidLinkText),
        };
    }

    /// <summary>Runs the work once the response has been sent (the request's services are still alive). Overridden by tests.</summary>
    protected virtual void AfterResponse(Func<Task> work) => Response.OnCompleted(work);
}
