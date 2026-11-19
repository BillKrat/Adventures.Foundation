using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Adventures.Collaboration;

namespace Adventures.Blog;

/// <summary>
/// One call that wires the blog feature: the Blls, presenters and services for posts, pages, categories, tags, site settings, public
/// content and authoring. It is store-agnostic: the host separately registers where each blog entity type is stored (today
/// <c>AddNQuadEntityRepository</c> per type, which also registers the Dal) and the host decides which Dal serves it (S7: legacy SQL).
/// </summary>
public static class BlogServiceCollectionExtensions
{
    public static IServiceCollection AddBlog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPageBll, PageBll>();
        services.AddScoped<IPostBll, PostBll>();
        services.AddScoped<IContentAuthoring, ContentAuthoring>();
        services.AddScoped<IPublicContent, PublicContent>();
        services.AddScoped<ISiteSettings, SiteSettings>();
        services.AddScoped<IEntityPresenter<SiteSetting>, SiteSettingPresenter>();
        services.AddScoped<IEntityPresenter<Post>, PostPresenter>();
        services.AddScoped<IEntityBll<SiteSetting>>(p => new EntityBll<SiteSetting>(p.GetRequiredService<IEntityDal<SiteSetting>>()));
        services.AddScoped<IEntityBll<Category>>(p => new EntityBll<Category>(p.GetRequiredService<IEntityDal<Category>>()));
        services.AddScoped<IEntityBll<Tag>>(p => new EntityBll<Tag>(p.GetRequiredService<IEntityDal<Tag>>()));
        services.AddScoped<IEntityBll<VisitorComment>>(p => new EntityBll<VisitorComment>(p.GetRequiredService<IEntityDal<VisitorComment>>()));
        services.AddScoped<ICommentablePosts, PublicContentCommentablePosts>();
        services.AddScoped<IVisitorComments, VisitorComments>();
        services.AddScoped<IEntityPresenter<Category>>(p => new EntityPresenter<Category>(
            p.GetRequiredService<IEntityBll<Category>>(), p.GetRequiredService<EntitySchemaOf<Category>>().Schema, s => new Category(s), p.GetService<IEntityRelationProvider>()));
        services.AddScoped<IEntityPresenter<Tag>>(p => new EntityPresenter<Tag>(
            p.GetRequiredService<IEntityBll<Tag>>(), p.GetRequiredService<EntitySchemaOf<Tag>>().Schema, s => new Tag(s), p.GetService<IEntityRelationProvider>()));

        services.AddScoped<IEntityPresenter<Page>, PagePresenter>();

        // A conversation can hang under a post.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConversationTarget, PostConversationTarget>());
        services.AddScoped<CollaborationMigration>();
        services.AddScoped<ICollaborationMigration>(sp => sp.GetRequiredService<CollaborationMigration>());
        return services;
    }
}
