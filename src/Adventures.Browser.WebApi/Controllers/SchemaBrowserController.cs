using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Browser.WebApi;

/// <summary>
/// The schemas of this site, read only: their shape, never a row of data. Needs the <c>schema.read</c> right in the tenant of the request, and answers
/// 404 unless the host switched the browser on (<see cref="SchemaBrowserExtensions.AddSchemaBrowser"/>).
/// </summary>
[ApiController]
[Route("api/browser")]
[SchemaBrowserSwitchedOn]
[RequireRight(Rights.SchemaRead)]
public sealed class SchemaBrowserController : ControllerBase
{
    /// <summary>Which site answered, so a client can tell it reached the one it meant to.</summary>
    [HttpGet("context")]
    public ActionResult<SchemaBrowserContext> Context([FromServices] IRequestTenant tenant) =>
        Ok(new SchemaBrowserContext(tenant.Current.Name, tenant.Current.Domain));

    [HttpGet("schemas")]
    public async Task<ActionResult<SchemaCatalogList>> Schemas([FromServices] ISchemaCatalogSource source, CancellationToken cancellationToken) =>
        Ok((await source.LoadAsync(cancellationToken)).List());

    /// <summary>One schema. Its id is an IRI, so it travels in the query.</summary>
    [HttpGet("schema")]
    public async Task<ActionResult<SchemaDetail>> Schema([FromQuery] string id, [FromServices] ISchemaCatalogSource source, CancellationToken cancellationToken) =>
        (await source.LoadAsync(cancellationToken)).Find(id) is { } schema ? Ok(schema) : NotFound();
}
