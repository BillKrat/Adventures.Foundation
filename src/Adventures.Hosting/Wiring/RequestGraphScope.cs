using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.WebApi;

namespace Adventures.Hosting;

/// <summary>
/// The graph scope of one request (T2): the id of the tenant the server resolved from the host it saw. Every tenant has its own graph; there is no primary tenant
/// and no shared fallback, so a host with no tenant gets <see cref="TenantRequiredException"/> (a 404) instead of anyone's content. Nothing here reads a header
/// the caller controls.
/// </summary>
public sealed class RequestGraphScope(IRequestTenant requestTenant) : IGraphScope, ITenantContext
{
    public string TenantId => TenantKey;

    public string TenantKey =>
        requestTenant.Current.Id is { Length: > 0 } id && !string.IsNullOrWhiteSpace(id) ? id : throw new TenantRequiredException();
}
