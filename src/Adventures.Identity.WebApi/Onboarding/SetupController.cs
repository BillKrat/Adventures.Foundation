using System.Security.Claims;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

public sealed record SetupStatus(string Host, bool NeedsSetup);

public sealed record SetupSignIn(string? Site, string? UserName, string? Password);

public sealed record SetupToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

public sealed record SetupSite(string Id, string Name, string Domain);

public sealed record SetupAttach(string? TenantId);

public sealed record SetupNewSite(string? Name);

/// <summary>
/// Onboarding stage 3: a host that reaches no tenant (while others exist) shows a setup page. A platform administrator of an existing site signs in there with that site's own
/// sign-in, then attaches the host to a site as an alias or starts a new site. The sign-in gives a setup token: no tenant, no roles, no real name, accepted only by this API (the
/// setup token guard refuses it everywhere else), and every call checks again that the host still reaches nothing and the person is still a platform administrator.
/// Threats addressed: anyone adding a host to a tenant, a setup token used on a tenant, the site list shown before sign-in, password guessing (rate limited, one answer for every failure).
/// </summary>
[ApiController]
[Route("api/setup")]
[Authorize]
public sealed class SetupController(
    ITenantAliasEditor tenants, ITenantResolver resolver, IUserAccountService accounts, IUserBll people, IUserMembership membership,
    IPlatformAdministrators platform, IJwtTokenService tokens, ISiteProvisioner provisioner, TenantCacheInvalidator caches) : ApiControllerBase
{
    private string Host => TenantHosts.Normalize(Request.Host.Host);

    private string SiteDomain => FirstRunSite.SiteDomainOf(Host);

    /// <summary>True when this host is a real name, reaches no tenant and there is a site to attach it to.</summary>
    private async Task<bool> NeedsSetupAsync(CancellationToken cancellationToken) =>
        TenantHosts.IsRealName(SiteDomain)
        && (await resolver.ResolveAsync(Request.Host.Host, null, cancellationToken).ConfigureAwait(false)).Id is null
        && (await tenants.ListAsync(cancellationToken).ConfigureAwait(false)).Count > 0;

    /// <summary>Whether this host needs setting up. Names no site.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(new SetupStatus(Host, await NeedsSetupAsync(cancellationToken).ConfigureAwait(false)));

    /// <summary>Signs a platform administrator in to a site they administer, for setup only. Every failure is the same 401.</summary>
    [HttpPost("token")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RequestHardening.TokenPolicy)]
    [AllowAnonymous]
    public async Task<IActionResult> Token([FromBody] SetupSignIn request, CancellationToken cancellationToken)
    {
        if (!await NeedsSetupAsync(cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        var site = TenantHosts.Normalize(request.Site);
        if (site.Length == 0 || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
        {
            return Unauthorized();
        }

        var tenant = (await tenants.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => TenantHosts.Normalize(t.Domain) == site);
        if (tenant is null)
        {
            return Unauthorized();
        }

        var login = await accounts.LoginAsync(tenant.Domain, request.UserName.Trim(), request.Password, cancellationToken).ConfigureAwait(false);
        if (!login.Succeeded)
        {
            return Unauthorized();
        }

        if (login.MustChangePassword)
        {
            return Conflict(new { error = "password_change_required", title = "Sign in to that site and change the default password first." });
        }

        var members = await membership.MemberIdsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        var person = (await people.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(user => user.EntityId is { } id && members.Contains(id.Value)
                && string.Equals(user.GetValue("UserName")?.ToString(), request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (person?.EntityId is not { } personId || !await platform.IsAsync(personId.Value, cancellationToken).ConfigureAwait(false))
        {
            return Unauthorized();
        }

        var issued = tokens.IssueToken("setup-session",
        [
            new Claim(ClaimTypes.Name, "setup-session"),
            new Claim(WellKnownClaims.Setup, "platform"),
            new Claim(WellKnownClaims.SetupPerson, personId.Value),
        ]);
        return Ok(new SetupToken(issued.AccessToken, issued.ExpiresAtUtc));
    }

    /// <summary>The sites a setup token may attach this host to.</summary>
    [HttpGet("sites")]
    public Task<IActionResult> Sites(CancellationToken cancellationToken) =>
        SetupAsync(async () => Ok((await tenants.ListAsync(cancellationToken).ConfigureAwait(false)).Select(t => new SetupSite(t.Id, t.Name, t.Domain)).ToList()), cancellationToken);

    /// <summary>Makes this host an alias of the chosen site.</summary>
    [HttpPost("attach")]
    public Task<IActionResult> Attach([FromBody] SetupAttach request, CancellationToken cancellationToken) =>
        SetupAsync(() => GuardedAsync(async () =>
        {
            var updated = await tenants.AddAsync(request.TenantId ?? string.Empty, SiteDomain, cancellationToken).ConfigureAwait(false);
            caches.Invalidate();   // the host works at once
            return updated is null ? NotFound() : Ok(new { Host = SiteDomain, Aliases = updated });
        }), cancellationToken);

    /// <summary>Starts a new site on this host's domain, with its own Admin (default password, changed at the first sign-in).</summary>
    [HttpPost("new")]
    public Task<IActionResult> NewSite([FromBody] SetupNewSite request, CancellationToken cancellationToken) =>
        SetupAsync(() => GuardedAsync(async () =>
        {
            var created = await provisioner.CreateAsync(SiteDomain, request.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
            caches.Invalidate();
            return StatusCode(StatusCodes.Status201Created, created);
        }), cancellationToken);

    /// <summary>The checks every setup call makes: the host still reaches nothing, the caller holds a setup token, and that person is still a platform administrator.</summary>
    private async Task<IActionResult> SetupAsync(Func<Task<IActionResult>> action, CancellationToken cancellationToken)
    {
        if (!await NeedsSetupAsync(cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        var person = User.FindFirst(WellKnownClaims.SetupPerson)?.Value;
        if (User.FindFirst(WellKnownClaims.Setup)?.Value != "platform" || string.IsNullOrEmpty(person)
            || !await platform.IsAsync(person, cancellationToken).ConfigureAwait(false))
        {
            return Forbid();
        }

        return await action().ConfigureAwait(false);
    }
}
