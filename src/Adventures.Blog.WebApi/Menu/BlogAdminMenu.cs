using Adventures.Entities;
using Adventures.WebApi;
using Adventures.Collaboration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Blog.WebApi;

/// <summary>Collaboration-category posts that need the human's review (the badge beside Posts).</summary>
public sealed class PostsNeedingReviewCount(IPublicContent content) : IAdminCountSource
{
    private const int PageSize = 50;

    public string Id => "posts";

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var waiting = 0;
        for (var page = 1; ; page++)
        {
            var list = await content.ListPostsAsync(page, PageSize, PostApproval.CollaborationCategory, tag: null, cancellationToken, includeDrafts: true);
            waiting += list.Posts.Count(post => post.Approval == PostApproval.NeedsReview);
            if (page * PageSize >= list.Total || list.Posts.Count == 0)
            {
                return waiting;
            }
        }
    }
}

/// <summary>Visitor comments waiting for approval.</summary>
public sealed class CommentsPendingCount(IVisitorComments comments) : IAdminCountSource
{
    public string Id => "comments";

    public Task<int> CountAsync(CancellationToken cancellationToken) => comments.CountPendingAsync(cancellationToken);
}
/// <summary>The admin menu entries this library's screens own (R3 of the host reuse review).</summary>
public static class BlogAdminMenu
{
    public static IServiceCollection AddBlogAdminMenu(this IServiceCollection services)
    {
        services.AddAdminMenuItems(
            new AdminMenuItemDefinition("Dashboard", "dashboard", "Dashboard", "/admin/dashboard", "dashboard", RequiredRight: Rights.AdminAccess, Order: 1),
            new AdminMenuItemDefinition("Content", "posts", "Posts", "/admin/posts", "article", RequiredRight: Rights.PostsRead, Order: 1),
            new AdminMenuItemDefinition("Content", "comments", "Comments", "/admin/comments", "comment", RequiredRight: Rights.CommentsModerate, Order: 6),
            new AdminMenuItemDefinition("Content", "pages", "Pages", "/admin/pages", "description", RequiredRight: Rights.PagesRead, Order: 3),
            new AdminMenuItemDefinition("Content", "categories", "Categories", "/admin/categories", "folder", RequiredRight: Rights.PostsRead, Order: 4),
            new AdminMenuItemDefinition("Content", "tags", "Tags", "/admin/tags", "sell", RequiredRight: Rights.PostsRead, Order: 5),
            new AdminMenuItemDefinition("System", "settings", "Settings", "/admin/settings", "settings", RequiredRight: Rights.SettingsManage, Order: 2),
            new AdminMenuItemDefinition("System", "mail", "E-mail", "/admin/plugins/mail", "mail", RequiredRight: Rights.PluginsManage, Order: 4),
            new AdminMenuItemDefinition("System", "maintenance", "Maintenance", "/admin/maintenance", "build", RequiredRight: Rights.SettingsManage, Order: 8),
            new AdminMenuItemDefinition("System", "widgets", "Widgets", "/admin/widgets", "widgets", RequiredRight: Rights.SettingsManage, Order: 5),
            new AdminMenuItemDefinition("System", "languages", "Languages", "/admin/languages", "translate", RequiredRight: Rights.SettingsManage, Order: 6));
        services.AddAdminCount<PostsNeedingReviewCount>();
        services.AddAdminCount<CommentsPendingCount>();
        return services;
    }
}

