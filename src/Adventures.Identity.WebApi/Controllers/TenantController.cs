using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Which tenant this visit is for: the one the server decided from the host it saw (<see cref="IRequestTenant"/>, set by the tenancy middleware). One
/// deployed app serves every tenant's domain; the site is on the tenant's domain and this API on a subdomain of it, so the API's own host names the
/// tenant. Origin and Referer are never read: the caller sets them, and they used to be what this showed. Which host names reach which tenant (aliases) is
/// the platform administrator's to read and change: a host name decides whose data a request sees.
/// </summary>
[ApiController]
[Route("api/tenant")]
[Authorize]
public sealed class TenantController(IRequestTenant requestTenant, TenantCacheInvalidator? caches = null) : ApiControllerBase
{
    [HttpGet("current")]
    public IActionResult Current()
    {
        var current = requestTenant.Current;
        return Ok(new
        {
            current.Host,
            Tenant = current.Id is null ? null : new { current.Id, current.Name, current.Domain },
            // while the host mirrors another site: the site the host itself belongs to (its pointer is set on that one)
            Own = current.OwnId is null ? null : new { Id = current.OwnId, Domain = current.OwnDomain },
            Match = current.Match.ToString(),
        });
    }

    /// <summary>
    /// The tenants the signed-in person may act in (the switcher's list): those they belong to, or every tenant for a platform administrator. Choosing one is the
    /// <c>X-Acting-Tenant</c> header on later requests, which the server checks again each time.
    /// </summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromServices] IActingTenants acting, CancellationToken cancellationToken) =>
        Ok(new { ActingId = requestTenant.Current.Id, Tenants = await acting.ForAsync(User.Identity?.Name ?? string.Empty, cancellationToken) });

    /// <summary>Every tenant with the host names that reach it (its domain and its aliases).</summary>
    [HttpGet("hosts")]
    [RequireRight(Rights.PlatformManage)]
    public async Task<IActionResult> Hosts([FromServices] ITenantAliasEditor aliases, CancellationToken cancellationToken) =>
        Ok(await aliases.ListAsync(cancellationToken));

    /// <summary>Creates a tenant on a domain: a name and the domain, from one place. 409 with the reason when the name or domain is refused or another tenant already reaches the domain.</summary>
    [HttpPost]
    [RequireRight(Rights.PlatformManage)]
    public Task<IActionResult> CreateTenant([FromBody] NewTenantRequest request, [FromServices] ITenantCreator creator, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var created = await creator.CreateAsync(request.Name ?? string.Empty, request.Domain ?? string.Empty, cancellationToken);
            caches?.Invalidate(); // the new domain works at once
            return StatusCode(StatusCodes.Status201Created, created);
        });

    /// <summary>
    /// Sets or clears a tenant's mirror pointer ("use the configuration of"): the tenant's own record and data are untouched, only ignored while it is set. 409 with the reason for a target that is
    /// unknown, the tenant itself or already mirroring, or a tenant that others mirror. It is refused when the request is on the very site it would change (set it from another site's address, or
    /// while acting in that tenant), because the person would then be signed out of their own account with no way back; clearing is always allowed.
    /// </summary>
    [HttpPut("{id}/mirror")]
    [RequireRight(Rights.PlatformManage)]
    public Task<IActionResult> SetMirror(string id, [FromBody] MirrorRequest request, [FromServices] ITenantMirrors mirrors, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var current = requestTenant.Current;
            if (!string.IsNullOrWhiteSpace(request.Target) && current.Id == id && current.Match is TenantMatch.Domain or TenantMatch.Fallback)
            {
                throw new BusinessRuleException("You are on this site's own address, so making it mirror another site would sign you out of your own account with no way back (you would be locked out). Set it from the other site's address instead.");
            }

            var done = await mirrors.SetAsync(id, request.Target, cancellationToken);
            caches?.Invalidate();   // the host serves the other site at once
            return done ? NoContent() : NotFound();
        });

    /// <summary>Makes the host an alias of the tenant, so it mirrors the tenant's own domain. 409 when the name is not a plain host name or already reaches another tenant.</summary>
    [HttpPut("{id}/aliases")]
    [RequireRight(Rights.PlatformManage)]
    public Task<IActionResult> AddAlias(string id, [FromBody] AliasRequest request, [FromServices] ITenantAliasEditor aliases, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var updated = await aliases.AddAsync(id, request.Host, cancellationToken);
            caches?.Invalidate(); // the new name works at once, not after the kept answers expire
            return updated is null ? NotFound() : Ok(updated);
        });

    /// <summary>Takes the alias away; the host stops reaching the tenant (at once: the kept answers are cleared).</summary>
    [HttpDelete("{id}/aliases/{host}")]
    [RequireRight(Rights.PlatformManage)]
    public Task<IActionResult> RemoveAlias(string id, string host, [FromServices] ITenantAliasEditor aliases, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            TenantAddressGuard.ThrowIfCurrent(requestTenant.Current.Host, host);   // never cut the person off from the address they are using
            var updated = await aliases.RemoveAsync(id, host, cancellationToken);
            caches?.Invalidate();
            return updated is null ? NotFound() : Ok(updated);
        });

    /// <summary>
    /// Deletes a tenant (a soft delete: its domain and host names reach nothing afterwards). The tenant's own domain must be given as <c>confirm</c>. Refused (409, with the
    /// reason) for the tenant the person is working in, the tenant that owns the host this request came on, and the last tenant. The content stays in its graph, unreachable.
    /// </summary>
    [HttpDelete("{id}")]
    [RequireRight(Rights.PlatformManage)]
    public Task<IActionResult> DeleteTenant(string id, [FromQuery] string? confirm, [FromServices] ITenantRemover remover, [FromServices] ITenantResolver resolver, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var keep = new List<string>();
            if (requestTenant.Current.Id is { } working)
            {
                keep.Add(working);
            }

            // the tenant that owns the host this request came on: deleting it would leave the site's own address with no tenant
            if ((await resolver.ResolveAsync(requestTenant.Current.Host, null, cancellationToken)).Id is { } owner)
            {
                keep.Add(owner);
            }

            var removed = await remover.RemoveAsync(id, confirm, keep, cancellationToken);
            caches?.Invalidate();
            return removed is null ? NotFound() : Ok(removed);
        });
}

/// <summary>The name and domain of a new tenant.</summary>
public sealed record NewTenantRequest(string? Name, string? Domain);

/// <summary>The host name to add as an alias.</summary>
public sealed record AliasRequest(string Host);

/// <summary>The tenant to mirror (its id); empty or null clears the pointer.</summary>
public sealed record MirrorRequest(string? Target);
