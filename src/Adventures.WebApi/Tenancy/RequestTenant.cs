using Adventures.Entities;

namespace Adventures.WebApi;

/// <summary>The tenant this request is for, as the server decided from the host it saw (see <see cref="TenantMiddleware"/>).</summary>
public interface IRequestTenant
{
    CurrentTenant Current { get; }
}

/// <summary>Per-request holder filled by <see cref="TenantMiddleware"/>; before that it says "no tenant".</summary>
public sealed class RequestTenant : IRequestTenant, IHomeTenant
{
    public CurrentTenant Current { get; private set; } = new(null, null, null, string.Empty, TenantMatch.None);

    /// <summary>The tenant of the host the request came on. Set with every tenant the server decided itself; a tenant the person only chose to act in (the switcher) does not replace it.</summary>
    public string? HomeTenantId { get; private set; }

    public void Set(CurrentTenant current)
    {
        Current = current;
        if (current.Match != TenantMatch.Acting)
        {
            HomeTenantId = current.Id;
        }
    }
}
