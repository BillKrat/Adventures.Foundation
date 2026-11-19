// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Self-service profile: same routes as before ("me"). The caller is resolved by Username (the
/// JWT's Name claim, same one AuthController.WhoAmI reads), not the "sub" GUID - that GUID belongs
/// to the older Postgres-identity user record, a different id space from the new
/// Adventures.Entities.User's own entity id.
/// </summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController(IUserPresenter presenter) : EntityControllerBase<User>
{
    [HttpGet("me")]
    public async Task<ActionResult<EntityFormModel>> Me(CancellationToken cancellationToken)
    {
        var (current, failure) = await ResolveCurrentAsync(cancellationToken);
        return current is null ? failure! : Ok(current);
    }

    [HttpPut("me")]
    public async Task<ActionResult<EntityFormModel>> UpdateMe([FromBody] EntityDataModel request, CancellationToken cancellationToken)
    {
        var (current, failure) = await ResolveCurrentAsync(cancellationToken);
        return current is null ? failure! : await UpdateAsync(presenter, current.Entity.EntityId, request, cancellationToken);
    }

    /// <summary>
    /// Demonstrates the delete-own-account guard end to end: this always targets the caller's own
    /// id, so IUserBll.DeleteAsync always rejects it. A future "deactivate my account" feature
    /// (noted as later work) is a different, deliberate action - not this endpoint.
    /// </summary>
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
    {
        var (current, failure) = await ResolveCurrentAsync(cancellationToken);
        if (current is null)
        {
            return failure!;
        }

        return await GuardedAsync(async () =>
        {
            await presenter.DeleteAsync(current.Entity.EntityId, current.Entity.EntityId, cancellationToken);
            return NoContent();
        });
    }

    /// <summary>The caller's own form, or the failure result to return (401 no usable name in the token, 404 no such user).</summary>
    private async Task<(EntityFormModel? Current, ActionResult? Failure)> ResolveCurrentAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return (null, Unauthorized());
        }

        var form = await presenter.GetFormByUserNameAsync(userName, cancellationToken);
        return form is null ? (null, NotFound()) : (form, null);
    }
}
