using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Adventures.Collaboration;
using Adventures.MEF;

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
        services.TryAddSingleton<SiteOptions>();
        services.AddScoped<ISiteSettings, SiteSettings>();
        services.AddScoped<IContentExporter, ContentExporter>();
        services.AddScoped<IContentSearcher, ContentSearcher>();

        // HTML sanitising is a plug-in that is on by default, behind a seam an owner can switch off and a developer can replace (register another IHtmlContentSanitizer after this).
        services.AddPlugins();
        services.AddPlugin(HtmlSanitizerPlugin.Definition);
        services.AddMail();
        services.AddScoped<IHtmlContentSanitizer, HtmlContentSanitizer>();

        // Widgets: code that returns HTML for one of the four zones; where each sits is stored per tenant (the host registers the placement repository).
        services.AddWidgets();
        services.AddScoped<IEntityBll<WidgetPlacement>>(p => new EntityBll<WidgetPlacement>(p.GetRequiredService<IEntityDal<WidgetPlacement>>()));
        services.AddScoped<IWidgetPlacements, WidgetPlacements>();
        services.AddScoped<IEntityBll<LanguageFile>>(p => new EntityBll<LanguageFile>(p.GetRequiredService<IEntityDal<LanguageFile>>()));
        services.AddScoped<ILanguageFiles, LanguageFiles>();
        services.AddServerText();
        services.AddPlugin(WidgetsPlugin.Definition);
        services.AddWidget<TextBoxWidget>(WidgetsPlugin.Key, TextBoxWidget.Definition);
        services.AddWidget<TagCloudWidget>(WidgetsPlugin.Key, TagCloudWidget.Definition);
        services.AddWidget<CategoryListWidget>(WidgetsPlugin.Key, CategoryListWidget.Definition);
        services.AddWidget<AdministrationWidget>(WidgetsPlugin.Key, AdministrationWidget.Definition);

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
