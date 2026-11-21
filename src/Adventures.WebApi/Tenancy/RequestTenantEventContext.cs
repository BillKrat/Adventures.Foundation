using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

/// <summary>
/// Carries the tenant of the request that raised a plug-in event to the background scope that delivers it, so the handlers' tenant-scoped services
/// (their settings, the plug-in switches) answer for the right tenant. Register it as the host's <see cref="IPluginEventContext"/>.
/// </summary>
public sealed class RequestTenantEventContext : IPluginEventContext
{
    public PluginEventSnapshot Capture(IServiceProvider requestServices)
    {
        var current = requestServices.GetService<IRequestTenant>()?.Current;
        return new PluginEventSnapshot(string.IsNullOrWhiteSpace(current?.Domain) ? current?.Name : current.Domain, current);
    }

    public void Restore(IServiceProvider deliveryServices, PluginEventSnapshot snapshot)
    {
        if (snapshot.State is Adventures.Entities.CurrentTenant tenant)
        {
            deliveryServices.GetService<RequestTenant>()?.Set(tenant);
        }
    }
}
