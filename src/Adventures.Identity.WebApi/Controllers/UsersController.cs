// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Identity;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Full CRUDL over User - administrators only (the TenantAdmin role; every other signed-in user is refused with 403,
/// self-service stays on <see cref="ProfileController"/>), on the schema-driven Adventures.Entities/NQuadEntityRepository stack. The
/// only business rule (you cannot delete your own account) lives in IUserBll, not here; this
/// controller only resolves the current user id (from the same claim AuthController.WhoAmI reads)
/// to pass to it. Get/List/Create/Update delegate straight to the promoted EntityControllerBase{User}
/// helpers from Adventures.WebApi - Delete stays bespoke since the guard needs the acting user id.
/// </summary>
[ApiController]
[Route("api/users")]
[RequireRight(Rights.UsersManage)]
[ServiceFilter(typeof(PlatformUserVisibilityFilter))]
public sealed class UsersController(
    ITenantUserPresenter presenter, IEntityRelationWriter relationWriter, EntitySchemaOf<User> userSchema, IUserMembership membership, ITenantContext tenantContext, IEntityRelationProvider relations,
    IUserProvisioning provisioning, IConfiguration configuration, Adventures.WebApi.IRequestTenant requestTenant, ILogger<UsersController> logger, UserVisibility? visibility = null)
    : EntityControllerBase<User>
{
    /// <summary>
    /// The people of this tenant. A platform administrator may ask for everyone (<c>filter=Scope&amp;filterValue=Everyone</c>) to find a person of another tenant and give them a role here;
    /// anyone else asking is simply given this tenant's people.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<EntityGridModel>> List([FromQuery] EntityGridQuery query, CancellationToken cancellationToken)
    {
        var scoped = string.Equals(query.Filter, ScopeFilter, StringComparison.Ordinal);
        var grid = await presenter.GetGridAsync(scoped ? query with { Filter = null, FilterValue = null } : query, cancellationToken);
        if (scoped && visibility?.SeeEveryone == true && grid.Paging is { } paging)
        {
            grid = grid with { Paging = paging with { Filter = ScopeFilter, FilterValue = EveryoneValue } };
        }

        grid = await WithRolesColumnAsync(grid, cancellationToken);
        return Ok(grid.WithLinks(BuildGridLinks(grid, Request.Path.Value ?? string.Empty)));
    }

    /// <summary>Adds the Roles column (who holds which role in the tenant being worked in, as the legacy blog shows it) to the page of people.</summary>
    private async Task<EntityGridModel> WithRolesColumnAsync(EntityGridModel grid, CancellationToken cancellationToken)
    {
        var roleNames = (await relations.GetOptionsAsync(IdentityConstants.RoleSchemaIri, null, cancellationToken)).ToDictionary(o => o.Id, o => o.Text, StringComparer.Ordinal);
        var byPerson = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in grid.Rows)
        {
            var assignments = await relations.GetRelationsAsync(userSchema.Schema, row.EntityId, editable: false, cancellationToken);
            byPerson[row.EntityId] = UserRolesColumn.NamesOf(assignments.SelectMany(a => a.Grid.Rows), tenantContext.TenantId, roleNames);
        }

        return UserRolesColumn.Add(grid, byPerson);
    }

    public const string ScopeFilter = "Scope";

    public const string EveryoneValue = "Everyone";

    /// <summary>A person of this tenant, or anyone when the platform administrator has been allowed to see everyone for this request.</summary>
    private async Task<bool> IsVisibleAsync(string id, CancellationToken cancellationToken) =>
        visibility?.SeeEveryone == true || (await membership.MemberIdsAsync(tenantContext.TenantId, cancellationToken)).Contains(id);

    /// <summary>The blank form for adding a person (the user name is typed here and fixed afterwards).</summary>
    [HttpGet("new")]
    public async Task<ActionResult<EntityFormModel>> New(CancellationToken cancellationToken) =>
        await presenter.GetNewFormAsync(cancellationToken) is { } blank ? Ok(blank) : NotFound();

    [HttpGet("{id}")]
    public async Task<ActionResult<EntityFormModel>> Get(string id, CancellationToken cancellationToken)
    {
        var form = await presenter.GetFormAsync(id, cancellationToken);
        if (form is null)
        {
            return NotFound();
        }

        var path = Request.Path.Value!.TrimEnd('/');
        return Ok(WithRelationLinks(form, path) with
        {
            Actions =
            [
                new EntityAction(
                    "set-password", "Set password", $"{path}/set-password",
                    "Generate a new password for this person? It is shown once, replaces the old one, and must be changed at the first sign-in."),
            ],
        });
    }

    /// <summary>
    /// Gives the person a way to sign in: creates their identity account when they have none (without roles) and sets a generated first
    /// password, returned once and never stored or logged in the clear. The person must change it at the first sign-in.
    /// </summary>
    [HttpPost("{id}/set-password")]
    public async Task<IActionResult> SetPassword(string id, CancellationToken cancellationToken)
    {
        var form = await presenter.GetFormAsync(id, cancellationToken);
        if (form is null)
        {
            return NotFound();
        }

        string Value(string name) => form.Entity.Values.FirstOrDefault(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
        var userName = Value("UserName");
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Problem(title: "This person has no user name, so there is nothing to sign in with.", statusCode: StatusCodes.Status400BadRequest);
        }

        // A person of several tenants has one sign-in for all of them: resetting it from one tenant would let that tenant's administrator sign in as them everywhere.
        if ((await membership.TenantIdsOfAsync(id, cancellationToken)).Any(other => !string.Equals(other, tenantContext.TenantId, StringComparison.Ordinal)))
        {
            return Problem(title: "This person also belongs to another tenant, so their password cannot be reset from here: it would change their sign-in for every tenant.", statusCode: StatusCodes.Status409Conflict);
        }

        var tenant = Adventures.WebApi.SiteDomain.Tenant(requestTenant, configuration, "global-webnet.com");
        ProvisionedLogin login;
        try
        {
            login = await provisioning.SetInitialPasswordAsync(tenant, userName, Value("Email"), Value("DisplayName"), cancellationToken);
        }
        catch (NotSupportedException refusal)
        {
            return Problem(title: refusal.Message, statusCode: StatusCodes.Status501NotImplemented);
        }

        logger.LogInformation("Password set for {UserName} (account created: {Created}) by {Administrator}", login.Username, login.AccountCreated, User.Identity?.Name);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { login.Username, login.Password, login.AccountCreated, MustChangeAtFirstSignIn = true });
    }

    /// <summary>Whether the person is a platform administrator, and whether the caller may change it (only a platform administrator may). The switch is the person's, not this tenant's.</summary>
    [HttpGet("{id}/platform")]
    public async Task<IActionResult> GetPlatform(
        string id, [FromServices] IPlatformAdministrators platform, [FromServices] IRightsAuthorizer authorizer, CancellationToken cancellationToken)
    {
        if (!await IsVisibleAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return Ok(new PlatformState(
            await platform.IsAsync(id, cancellationToken), await authorizer.IsAllowedAsync(User, Rights.PlatformManage, cancellationToken)));
    }

    /// <summary>
    /// Switches the person on or off as a platform administrator. Needs the platform right itself (a tenant administrator cannot grant it, threat: self-granted platform power),
    /// and the last platform administrator cannot be switched off (409).
    /// </summary>
    [HttpPut("{id}/platform")]
    [RequireRight(Rights.PlatformManage)]
    public async Task<IActionResult> SetPlatform(string id, [FromBody] PlatformRequest request, [FromServices] PlatformAdministratorBll bll, CancellationToken cancellationToken)
    {
        if (!await IsVisibleAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return await GuardedAsync(async () =>
        {
            var found = await bll.SetAsync(id, request.On, User.Identity?.Name ?? string.Empty, cancellationToken);
            logger.LogInformation("Platform administrator switch for {UserId} set to {On} by {Administrator}", id, request.On, User.Identity?.Name);
            return found ? NoContent() : NotFound();
        });
    }

    /// <summary>Adds a role assignment (or any child of a declared relation) to the user: each child is saved through its own endpoint and rules, never as part of the user.</summary>
    [HttpPost("{id}/relations/{relationName}")]
    public async Task<IActionResult> AddRelationChild(string id, string relationName, [FromBody] EntityDataModel request, CancellationToken cancellationToken)
    {
        if (!await IsVisibleAsync(id, cancellationToken))
        {
            return NotFound();
        }

        // An assignment may only be made inside the administrator's own tenant: the tenant (or the organisation's tenant) in the request is checked, never trusted.
        if (!await AssignmentIsHereAsync(relationName, request.Values, cancellationToken))
        {
            return Problem(title: "A role can only be assigned inside your own tenant.", statusCode: StatusCodes.Status403Forbidden);
        }

        return await AddRelationChildAsync(relationWriter, userSchema.Schema, id, relationName, request, cancellationToken);
    }

    [HttpDelete("{id}/relations/{relationName}/{childId}")]
    public async Task<IActionResult> RemoveRelationChild(string id, string relationName, string childId, CancellationToken cancellationToken)
    {
        if (!await IsVisibleAsync(id, cancellationToken))
        {
            return NotFound();
        }

        // Only an assignment of this tenant can be removed here: a person of several tenants keeps the roles they hold elsewhere.
        var assignments = await relations.GetRelationsAsync(userSchema.Schema, id, editable: false, cancellationToken);
        var child = assignments.Where(r => r.Name.Equals(relationName, StringComparison.OrdinalIgnoreCase)).SelectMany(r => r.Grid.Rows)
            .FirstOrDefault(row => string.Equals(row.EntityId, childId, StringComparison.Ordinal));
        if (child is null || !await AssignmentIsHereAsync(relationName, child.Values, cancellationToken))
        {
            return NotFound();
        }

        return await RemoveRelationChildAsync(relationWriter, userSchema.Schema, id, relationName, childId, cancellationToken);
    }

    /// <summary>True when a tenant-role or organisation-role assignment belongs to the current tenant (other relations are not tenant assignments and pass).</summary>
    private async Task<bool> AssignmentIsHereAsync(string relationName, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        string? Value(string name) => values.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        if (relationName.Equals("TenantRoles", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(Value("Tenant"), tenantContext.TenantId, StringComparison.Ordinal);
        }

        if (relationName.Equals("OrganizationRoles", StringComparison.OrdinalIgnoreCase))
        {
            return Value("Organization") is { Length: > 0 } organisation && await membership.OrganisationInTenantAsync(organisation, tenantContext.TenantId, cancellationToken);
        }

        return true;
    }

    [HttpPost]
    public Task<ActionResult<EntityFormModel>> Create([FromBody] EntityDataModel request, CancellationToken cancellationToken) =>
        CreateAsync(presenter, request, cancellationToken);

    [HttpPut("{id}")]
    public Task<ActionResult<EntityFormModel>> Update(string id, [FromBody] EntityDataModel request, CancellationToken cancellationToken) =>
        UpdateAsync(presenter, id, request, cancellationToken);

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var currentUserId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        return await GuardedAsync(async () =>
        {
            var deleted = await presenter.DeleteAsync(id, currentUserId, cancellationToken);
            return deleted ? NoContent() : NotFound();
        });
    }

    private async Task<string?> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var form = await presenter.GetFormByUserNameAsync(userName, cancellationToken);
        return form?.Entity.EntityId;
    }
}

/// <summary>The platform administrator switch of a person, and whether the caller may change it.</summary>
public sealed record PlatformState(bool IsPlatformAdministrator, bool CanChange);

/// <summary>The new state of the switch.</summary>
public sealed record PlatformRequest(bool On);
