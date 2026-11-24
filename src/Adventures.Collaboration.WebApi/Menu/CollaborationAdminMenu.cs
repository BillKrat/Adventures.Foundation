using Adventures.Entities;
using Adventures.WebApi;
using Adventures.Collaboration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Collaboration.WebApi;

/// <summary>Developer collaboration items waiting for the human's review (the menu badge beside Collaboration and a dashboard figure).</summary>
public sealed class CollaborationWaitingCount(ICollaborationBll items) : IAdminCountSource
{
    public string Id => "collaboration";

    public async Task<int> CountAsync(CancellationToken cancellationToken) =>
        (await items.ListAsync(cancellationToken)).Count(item =>
            item.GetValue("Kind") as string == CollaborationKinds.Developer && item.GetValue("Approval") as string == PostApproval.NeedsReview);
}
/// <summary>The admin menu entries this library's screens own (R3 of the host reuse review).</summary>
public static class CollaborationAdminMenu
{
    public static IServiceCollection AddCollaborationAdminMenu(this IServiceCollection services)
    {
        services.AddAdminMenuItems(
            new AdminMenuItemDefinition("Content", "collaboration", "Collaboration", "/admin/collaboration", "forum", RequiredRight: Rights.CollaborationRead, Order: 2, AnyOfPlugins: [CollaborationPlugins.DeveloperCollaboration, CollaborationPlugins.ResearchCollaboration]),
            new AdminMenuItemDefinition("Content", "decisions", "Decisions", "/admin/decisions", "gavel", RequiredRight: Rights.DecisionsRead, Order: 7, AnyOfPlugins: [CollaborationPlugins.Decisions]),
            new AdminMenuItemDefinition("System", "plugins", "Plugins", "/admin/plugins", "extension", RequiredRight: Rights.PluginsManage, Order: 3));
        services.AddAdminCount<CollaborationWaitingCount>();
        return services;
    }
}

