using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Browser.Host.Controllers;

/// <summary>Who may use the browser: the TenantAdmin role of a token issued with the shared signing key (the policy name follows <c>JwtServiceCollectionExtensions.RolePolicyName</c>).</summary>
public static class BrowserAuth
{
    /// <summary>The role a signed-in administrator's token carries (the legacy tenant administrator, which the main app treats as holding every right).</summary>
    public const string AdminRole = WellKnownClaims.TenantAdminRole;
    public const string Policy = WellKnownClaims.TenantAdminPolicy;
}

/// <summary>What the browser page shows for a kind of entity in its list of kinds.</summary>
public sealed record BrowserTypeInfo(string Key, string Title, string SchemaIri);

/// <summary>
/// The generic, schema-driven endpoints behind the browser page: the same grid and form models the main admin uses, for any registered kind
/// of entity. Signed-in administrators only (the TenantAdmin role of a token issued with the shared signing key). One presenter per request over the
/// kind's own registration; nothing here names a feature.
/// </summary>
[ApiController]
[Route("api/browser")]
[Authorize(Policy = BrowserAuth.Policy)]
public sealed class BrowserController(INQuadStore store, IReadOnlyList<BrowserType> types, IEntityRelationProvider relations)
    : EntityControllerBase<DynamicRecord>
{
    [HttpGet("types")]
    public ActionResult<IReadOnlyList<BrowserTypeInfo>> Types() =>
        Ok(types.Select(t => new BrowserTypeInfo(t.Key, t.Title, t.Registration.Schema.SchemaIri)).ToList());

    [HttpGet("{key}")]
    public async Task<ActionResult<EntityGridModel>> List(string key, [FromQuery] EntityGridQuery query, CancellationToken cancellationToken) =>
        Presenter(key) is { } presenter ? await GridAsync(presenter, query, cancellationToken) : NotFound();

    [HttpGet("{key}/new")]
    public async Task<ActionResult<EntityFormModel>> New(string key, CancellationToken cancellationToken) =>
        Presenter(key) is { } presenter ? await NewFormAsync(presenter, cancellationToken) : NotFound();

    [HttpGet("{key}/{id}")]
    public async Task<ActionResult<EntityFormModel>> Get(string key, string id, CancellationToken cancellationToken) =>
        Presenter(key) is { } presenter ? await GetAsync(presenter, id, cancellationToken) : NotFound();

    [HttpPost("{key}")]
    public async Task<ActionResult<EntityFormModel>> Create(string key, [FromBody] EntityDataModel request, CancellationToken cancellationToken) =>
        Presenter(key) is { } presenter ? await CreateAsync(presenter, request, cancellationToken) : NotFound();

    [HttpPut("{key}/{id}")]
    public async Task<ActionResult<EntityFormModel>> Update(string key, string id, [FromBody] EntityDataModel request, CancellationToken cancellationToken) =>
        Presenter(key) is { } presenter ? await UpdateAsync(presenter, id, request, cancellationToken) : NotFound();

    [HttpDelete("{key}/{id}")]
    public async Task<IActionResult> Delete(string key, string id, CancellationToken cancellationToken) =>
        Presenter(key) is { } presenter ? await DeleteEntityAsync(presenter, id, cancellationToken) : NotFound();

    private EntityPresenter<DynamicRecord>? Presenter(string key)
    {
        var type = types.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        if (type is null)
        {
            return null;
        }

        var registration = type.Registration;
        var repository = new NQuadEntityRepository<DynamicRecord>(
            store, registration.Schema, registration.BaseIri, registration.TypeIri, registration.Graph, schema => new DynamicRecord(schema));
        var bll = new EntityBll<DynamicRecord>(new EntityDal<DynamicRecord>(repository));
        return new EntityPresenter<DynamicRecord>(bll, registration.Schema, schema => new DynamicRecord(schema), relations);
    }
}
