using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Adventures.Identity.WebApi;

/// <summary>What the owner sends to finish a recovery: the code, the site (its domain; optional on a host that already reaches a site), the account and the password they chose.</summary>
public sealed record RecoveryComplete(string? Code, string? Site, string? UserName, string? NewPassword);

/// <summary>
/// Recovery without e-mail, for an owner locked out of a site: ask for a one-time code, put a file named with it into the App_Data folder on the host, then choose a new
/// password for a named account. It never resets to a default password and is never triggered by failed sign-ins (anyone could cause those). Until the file is proven the answer
/// is the same for every bad input, so nothing about sites or accounts leaks; both calls are rate limited like sign-in.
/// </summary>
[ApiController]
[Route("api/recovery")]
[AllowAnonymous]
public sealed class RecoveryController(IRecoveryProof proof, IPasswordResetter resetter, ITenantAliasEditor tenants, IRequestTenant requestTenant) : ApiControllerBase
{
    private const string NotValid = "The code is not valid, has expired, or its file was not found in the App_Data folder. Ask for a new code and try again.";

    /// <summary>Hands out a code and says which file to create. The app restarting forgets the code, so ask again if the site was restarted.</summary>
    [HttpPost("start")]
    [EnableRateLimiting(RequestHardening.TokenPolicy)]
    [RequestSizeLimit(1024)]
    public IActionResult Start() => Ok(proof.Start());

    /// <summary>Sets the chosen password for the account once the file is there. 400 for any unproven code, 422 for a weak password, 404 for an unknown site or account (only after the proof).</summary>
    [HttpPost("complete")]
    [EnableRateLimiting(RequestHardening.TokenPolicy)]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Complete([FromBody] RecoveryComplete request, CancellationToken cancellationToken)
    {
        if (!proof.IsProven(request.Code))
        {
            return BadRequest(NotValid);
        }

        var password = request.NewPassword ?? string.Empty;
        if (!PasswordPolicy.IsAcceptable(password, string.Empty))
        {
            return UnprocessableEntity("Choose a longer password: " + PasswordPolicy.Description);
        }

        var user = (request.UserName ?? string.Empty).Trim();
        if (user.Length == 0)
        {
            return BadRequest("Give the user name of the account to recover.");
        }

        string domain;
        if (!string.IsNullOrWhiteSpace(request.Site))
        {
            var site = TenantHosts.Normalize(request.Site);
            var found = (await tenants.ListAsync(cancellationToken)).FirstOrDefault(t => TenantHosts.Normalize(t.Domain) == site);
            if (found is null)
            {
                return NotFound("There is no site with that address.");
            }

            domain = found.Domain;
        }
        else if (requestTenant.Current.Domain is { Length: > 0 } own)
        {
            domain = own;
        }
        else
        {
            return BadRequest("Give the address of the site to recover (the domain the site is set up for).");
        }

        switch (await resetter.ResetAsync(domain, user, password, cancellationToken))
        {
            case ResetOutcome.Reset:
                proof.Consume(request.Code!);
                return NoContent();
            case ResetOutcome.Rejected:
                return UnprocessableEntity("Choose a longer password: " + PasswordPolicy.Description);
            default:
                return NotFound("There is no active account with that name on that site.");
        }
    }
}
