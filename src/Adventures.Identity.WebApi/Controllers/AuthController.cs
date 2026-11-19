// Assessed 2026-10-24 17:51 CT (Claude): automated scan for secrets, injection and unsafe patterns, re-run after the fixes - security findings F-1 to F-3 fixed in this file or in code it uses, covered by tests. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

public sealed record TokenRequest(string? UserName, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword);

/// <summary>
/// Issues JWTs after validating credentials against the real user store (Adventures.Data +
/// Adventures.Identity), replacing the config-seeded demo-user placeholder this controller used
/// to have - see docs/artifacts/Claude-2026-09-21-real-login-wired.md for why that replacement
/// was deferred until the underlying login path was verified end-to-end first.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IUserAccountService accountService, IConfiguration configuration, Adventures.WebApi.IRequestTenant requestTenant) : ControllerBase
{
    /// <summary>
    /// The tenant a person signs in to: the one the server resolved from the host it saw (T1, see the tenancy middleware), otherwise the configured
    /// one (<c>Site:Domain</c>, <c>Auth:Tenant</c>, default "global-webnet.com") exactly as before. The token carries it, and a token for another
    /// tenant is refused on this one's domain.
    /// </summary>
    private string Tenant => Adventures.WebApi.SiteDomain.Tenant(requestTenant, configuration, "global-webnet.com");

    [HttpPost("token")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Adventures.WebApi.RequestHardening.TokenPolicy)]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Token([FromBody] TokenRequest request)
    {
        var errors = ValidateCredentials(request);
        if (errors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest });
        }

        var result = await accountService.LoginAsync(Tenant, request.UserName!, request.Password!);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        return Ok(new TokenResponse(result.AccessToken!, result.ExpiresAtUtc!.Value, result.MustChangePassword));
    }

    /// <summary>
    /// Trust-boundary validation (AppArch: validate all input, server-side): a missing body or blank field is a
    /// client error (400), not a fault inside the login service. Says nothing about whether the account exists.
    /// </summary>
    private static Dictionary<string, string[]> ValidateCredentials(TokenRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request?.UserName))
        {
            errors[nameof(TokenRequest.UserName)] = ["UserName is required."];
        }

        if (string.IsNullOrWhiteSpace(request?.Password))
        {
            errors[nameof(TokenRequest.Password)] = ["Password is required."];
        }

        return errors;
    }

    /// <summary>
    /// The signed-in user changes their own password. The user is the token's subject (never a client-supplied id), the
    /// current password must be right (and counts toward the login lockout when it is not), and the new one must meet
    /// <see cref="PasswordPolicy"/>. A success clears the "must change password" flag, so the next login no longer asks.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request?.CurrentPassword))
        {
            errors[nameof(ChangePasswordRequest.CurrentPassword)] = ["Your current password is required."];
        }

        if (string.IsNullOrWhiteSpace(request?.NewPassword))
        {
            errors[nameof(ChangePasswordRequest.NewPassword)] = ["A new password is required."];
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest });
        }

        var subject = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var result = await accountService.ChangePasswordAsync(userId, request!.CurrentPassword!, request.NewPassword!, cancellationToken);
        return result switch
        {
            ChangePasswordResult.Succeeded => NoContent(),
            ChangePasswordResult.InvalidCurrentPassword => Rejected(nameof(ChangePasswordRequest.CurrentPassword), "Your current password is not right."),
            ChangePasswordResult.NewPasswordRejected => Rejected(nameof(ChangePasswordRequest.NewPassword), PasswordPolicy.Description),
            ChangePasswordResult.AccountLocked => Rejected(nameof(ChangePasswordRequest.CurrentPassword), "Too many wrong attempts. Try again in a few minutes."),
            _ => Unauthorized(),
        };
    }

    /// <summary>
    /// What the signed-in person may do: the rights of their roles, each with its description, so a person (and a test) can see exactly what
    /// the server will allow. Read only; it decides nothing by itself.
    /// </summary>
    [HttpGet("rights")]
    [Authorize]
    public async Task<IActionResult> MyRights([FromServices] Adventures.WebApi.IRightsAuthorizer authorizer, CancellationToken cancellationToken)
    {
        // The same answer the routes use: the rights in this request's tenant.
        var granted = await authorizer.RightsOfAsync(User, cancellationToken);
        return Ok(new
        {
            UserName = User.Identity?.Name,
            Rights = Adventures.Entities.Rights.Catalog.Where(r => granted.Contains(r.Name)).Select(r => new { r.Name, r.Description }),
        });
    }

    /// <summary>The 400 shape the client already turns into sentences (a field name and its messages).</summary>
    private BadRequestObjectResult Rejected(string field, string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }) { Status = StatusCodes.Status400BadRequest });

    /// <summary>Sample protected endpoint to prove JWT validation works end to end.</summary>
    [HttpGet("whoami")]
    [Authorize]
    public ActionResult<string> WhoAmI() => Ok(User.Identity?.Name ?? "unknown");
}
