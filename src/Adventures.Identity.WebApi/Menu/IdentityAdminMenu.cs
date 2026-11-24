using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Identity.WebApi;


/// <summary>The admin menu entries this library's screens own (R3 of the host reuse review).</summary>
public static class IdentityAdminMenu
{
    public static IServiceCollection AddIdentityAdminMenu(this IServiceCollection services)
    {
        services.AddAdminMenuItems(
            new AdminMenuItemDefinition("Users", "users", "Users", "/admin/users", "group", RequiredRight: Rights.UsersManage, Order: 1),
            new AdminMenuItemDefinition("Users", "roles", "Roles", "/admin/roles", "admin_panel_settings", RequiredRight: Rights.RolesManage, Order: 2),
            new AdminMenuItemDefinition("System", "tenants", "Tenants", "/admin/tenants", "domain", RequiredRight: Rights.PlatformManage, Order: 7));
        return services;
    }
}

