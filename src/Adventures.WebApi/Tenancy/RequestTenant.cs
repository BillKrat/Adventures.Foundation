using Adventures.Entities;

namespace Adventures.WebApi;

/// <summary>The tenant this request is for, as the server decided from the host it saw (see <see cref="TenantMiddleware"/>).</summary>
public interface IRequestTenant
{
    CurrentTenant Current { get; }
}

/// <summary>Per-request holder filled by <see cref="TenantMiddleware"/>; before that it says "no tenant".</summary>
public sealed class RequestTenant : IRequestTenant
{
    public CurrentTenant Current { get; private set; } = new(null, null, null, string.Empty, TenantMatch.None);

    public void Set(CurrentTenant current) => Current = current;
}
