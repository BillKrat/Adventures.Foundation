using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Diagnostics.WebApi;

/// <summary>The active exception count shown beside the Exceptions entry and on the dashboard.</summary>
public sealed class ExceptionsCount(IExceptionLogPresenter exceptions) : IAdminCountSource
{
    public string Id => "exceptions";

    public Task<int> CountAsync(CancellationToken cancellationToken) => exceptions.CountActiveAsync(cancellationToken);
}
/// <summary>The admin menu entries this library's screens own (R3 of the host reuse review).</summary>
public static class DiagnosticsAdminMenu
{
    public static IServiceCollection AddDiagnosticsAdminMenu(this IServiceCollection services)
    {
        services.AddAdminMenuItems(
            new AdminMenuItemDefinition("System", "exceptions", "Exceptions", "/admin/exceptions", "warning", RequiredRight: Rights.DiagnosticsRead, Order: 1));
        services.AddAdminCount<ExceptionsCount>();
        return services;
    }
}

