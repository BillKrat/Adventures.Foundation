using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Adventures.Identity.WebApi;

/// <summary>
/// An administrator (the users right) reads whether another person's saved address is confirmed and sends them the confirmation mail. The confirm link needs no sign-in, so a person who
/// cannot sign in can still prove the address and then use forgot-my-password. The mail goes only to the address saved on that person, and the link host must be a registered one.
/// </summary>
[ApiController]
[Route("api/users/{id}/email-verification")]
[RequireRight(Rights.UsersManage)]
[ServiceFilter(typeof(PlatformUserVisibilityFilter))]
public sealed class UserEmailVerificationController(ITenantUserPresenter presenter, EmailVerificationService verification, ITenantHostSource hosts, IHostEnvironment environment) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<VerificationStatus>> Status(string id, CancellationToken cancellationToken)
    {
        var address = await AddressOfAsync(id, cancellationToken);
        return address is null ? NotFound() : Ok(new VerificationStatus(address, await verification.IsVerifiedAsync(id, address, cancellationToken)));
    }

    [HttpPost("request")]
    public async Task<IActionResult> Request(string id, [FromBody] VerificationRequest request, CancellationToken cancellationToken)
    {
        var address = await AddressOfAsync(id, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        var linkBase = await ClientLinkBase.ResolveAsync(request.ClientBase, hosts, environment, cancellationToken);
        if (linkBase is null)
        {
            return BadRequest("The address of this site is not one we know. Open the site by its own address and try again.");
        }

        return await GuardedAsync(async () =>
        {
            await verification.RequestAsync(id, address, linkBase + "/verify-email", cancellationToken);
            return NoContent();
        });
    }

    /// <summary>The address saved on the person (empty when none), or null when the person is not in this tenant.</summary>
    private async Task<string?> AddressOfAsync(string id, CancellationToken cancellationToken)
    {
        var form = await presenter.GetFormAsync(id, cancellationToken);
        return form?.Entity.Values.FirstOrDefault(v => string.Equals(v.Key, "Email", StringComparison.OrdinalIgnoreCase)).Value?.Trim() ?? (form is null ? null : string.Empty);
    }
}
