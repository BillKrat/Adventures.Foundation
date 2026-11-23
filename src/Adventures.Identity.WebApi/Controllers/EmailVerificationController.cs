using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Adventures.Identity.WebApi;

public sealed record VerificationRequest(string? ClientBase);

public sealed record ConfirmVerification(string? User, string? Token);

public sealed record VerificationStatus(string Address, bool Verified);

/// <summary>
/// E-mail address verification for the signed-in person: ask for a link mail to the address saved on their profile (never to an address in the request), read whether that address is
/// verified, and, anonymously, open the link. The link base must be one of the registered hosts (or localhost in Development) so our mail cannot be turned into a phishing link.
/// </summary>
[ApiController]
[Route("api/email-verification")]
public sealed class EmailVerificationController(IUserPresenter presenter, EmailVerificationService verification, ITenantHostSource hosts, IHostEnvironment environment) : ApiControllerBase
{
    private const string FailureText = "This link is not valid any more. Ask for a new one from your profile.";

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<VerificationStatus>> Status(CancellationToken cancellationToken)
    {
        var current = await CurrentAsync(cancellationToken);
        if (current.Failure is not null)
        {
            return current.Failure;
        }

        return Ok(new VerificationStatus(current.Address, await verification.IsVerifiedAsync(current.UserId, current.Address, cancellationToken)));
    }

    [HttpPost("request")]
    [Authorize]
    public async Task<IActionResult> Request([FromBody] VerificationRequest request, CancellationToken cancellationToken)
    {
        var current = await CurrentAsync(cancellationToken);
        if (current.Failure is not null)
        {
            return current.Failure;
        }

        var linkBase = await LinkBaseAsync(request.ClientBase, cancellationToken);
        if (linkBase is null)
        {
            return BadRequest("The address of this site is not one we know. Open the site by its own address and try again.");
        }

        return await GuardedAsync(async () =>
        {
            await verification.RequestAsync(current.UserId, current.Address, linkBase + "/verify-email", cancellationToken);
            return NoContent();
        });
    }

    [HttpPost("confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> Confirm([FromBody] ConfirmVerification request, CancellationToken cancellationToken) =>
        await verification.ConfirmAsync(request.User ?? string.Empty, request.Token ?? string.Empty, cancellationToken) ? NoContent() : BadRequest(FailureText);

    private async Task<(string UserId, string Address, ActionResult? Failure)> CurrentAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return (string.Empty, string.Empty, Unauthorized());
        }

        var form = await presenter.GetFormByUserNameAsync(userName, cancellationToken);
        if (form is null)
        {
            return (string.Empty, string.Empty, NotFound());
        }

        var address = form.Entity.Values.FirstOrDefault(v => string.Equals(v.Key, "Email", StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
        return (form.Entity.EntityId, address.Trim(), null);
    }

    /// <summary>The scheme and host the client was opened at, accepted only for a registered host name (or localhost in Development), with no path, query or credentials.</summary>
    private async Task<string?> LinkBaseAsync(string? clientBase, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(clientBase, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        var local = uri.IsLoopback && environment.IsDevelopment();
        if (!local && !(await hosts.AllNamesAsync(cancellationToken)).Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!local && uri.Scheme != "https" && !uri.Host.EndsWith(".dtempurl.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".atempurl.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
