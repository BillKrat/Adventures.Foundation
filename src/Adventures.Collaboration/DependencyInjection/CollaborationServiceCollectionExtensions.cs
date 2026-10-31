using Adventures.Entities;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.Collaboration;

/// <summary>
/// One call that wires the collaboration feature: items (Developer and Research kinds), the shared conversation, the authoring surface and
/// its three plug-in definitions. Store-agnostic: the host registers where Collaboration items, Comments and plug-in states are stored.
/// </summary>
public static class CollaborationServiceCollectionExtensions
{
    public static IServiceCollection AddCollaboration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPlugins();
        foreach (var definition in CollaborationPlugins.Definitions)
        {
            services.AddPlugin(definition);
        }

        services.AddScoped<IEntityBll<Comment>>(p => new EntityBll<Comment>(p.GetRequiredService<IEntityDal<Comment>>()));
        services.AddScoped<IConversation, Conversation>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConversationTarget, CollaborationConversationTarget>());
        services.AddScoped<ICollaborationBll, CollaborationBll>();
        services.AddScoped<IEntityPresenter<CollaborationItem>, CollaborationPresenter>();
        services.AddScoped<ICollaborationAuthoring, CollaborationAuthoring>();
        return services;
    }
}
