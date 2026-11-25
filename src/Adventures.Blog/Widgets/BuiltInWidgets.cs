using System.Net;
using Adventures.MEF;

namespace Adventures.Blog;

/// <summary>The built-in plug-in that owns the widgets shipped with the blog; switch it off to hide them all.</summary>
public static class WidgetsPlugin
{
    public const string Key = "widgets";

    public static readonly PluginDefinition Definition = new(
        Key, "Widgets", "The widgets that come with the blog (text box, tag cloud, category list) and the Widgets screen where they are placed in the header, left, right and footer zones.", "1.0.0", 4);
}

/// <summary>A box of text or HTML the owner writes; it goes through the sanitiser plug-in like any widget.</summary>
public sealed class TextBoxWidget : IWidget
{
    public static readonly WidgetDefinition Definition = new(
        "text-box", "Text box", "A box of text or HTML you write yourself.",
        [new PluginSettingDefinition("Content", "Content", "The text or HTML to show.", PluginSettingKind.Lines, string.Empty, 4000)]);

    public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Settings.TryGetValue("Content", out var content) ? content.Trim() : string.Empty);
}

/// <summary>
/// The tag cloud as BlogEngine drew it: each tag a pill with its post count and a link to its posts, sized by how much it is used. Tags used by fewer
/// posts than the minimum (two by default) are left out. The sizes are classes (<c>tag-cloud__size-1</c> to <c>-5</c>) that the public page styles.
/// </summary>
public sealed class TagCloudWidget(IPublicContent content) : IWidget
{
    public static readonly WidgetDefinition Definition = new(
        "tag-cloud", "Tag cloud", "The tags of the blog as pills with their post counts, sized by use and linking to their posts.",
        [
            WidgetTerms.MaxSetting,
            new PluginSettingDefinition("Min", "Fewest posts", "Hide tags used by fewer posts than this (1 shows every tag).", PluginSettingKind.Number, "2"),
        ]);

    public async Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var min = int.TryParse(context.Settings.GetValueOrDefault("Min"), out var parsed) ? Math.Max(1, parsed) : 2;
        var shown = (await content.GetTagsAsync(cancellationToken).ConfigureAwait(false)).Where(t => t.Posts >= min).Take(WidgetTerms.MaxOf(context)).ToList();
        if (shown.Count == 0)
        {
            return string.Empty;
        }

        var (least, most) = (shown.Min(t => t.Posts), shown.Max(t => t.Posts));
        var items = shown.Select(t =>
        {
            var size = most == least ? 3 : 1 + (int)Math.Round((t.Posts - least) / (double)(most - least) * 4, MidpointRounding.AwayFromZero);
            var title = t.Posts == 1 ? "1 post" : $"{t.Posts} posts";
            return $"<li class=\"tag-cloud__item\"><a class=\"tag-cloud__tag tag-cloud__size-{size}\" href=\"/tag/{Uri.EscapeDataString(t.Name)}\" title=\"{title}\">{WebUtility.HtmlEncode(t.Name)}<span class=\"tag-cloud__count\">{t.Posts}</span></a></li>";
        });
        return $"<ul class=\"tag-cloud\" aria-label=\"Tags\">{string.Concat(items)}</ul>";
    }
}

/// <summary>
/// The Administration widget, as BlogEngine had it: the links of a signed-in administrator (Dashboard, Content, Custom, Users, Settings, Profile, Change password,
/// Log off). It needs the admin-access right, so the host leaves it out for everyone else; the links are fixed addresses, and each screen still checks its own right.
/// </summary>
public sealed class AdministrationWidget : IWidget
{
    /// <summary>The right that shows the widget (the value of <c>Rights.AdminAccess</c>; a host test keeps them equal).</summary>
    public const string AdminAccess = "admin.access";

    public static readonly WidgetDefinition Definition = new(
        "administration", "Administration", "The links of the admin area (and Profile, Change password and Log off), shown only to people who may use the admin area.",
        RequiredRight: AdminAccess);

    private static readonly (string Label, string Href)[] Links =
    [
        ("Dashboard", "/admin/dashboard"), ("Content", "/admin/posts"), ("Custom", "/admin/plugins"), ("Users", "/admin/users"),
        ("Settings", "/admin/settings"), ("Profile", "/profile"), ("Change password", "/profile#password"), ("Log off", "/logout"),
    ];

    public Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
        Task.FromResult($"<ul class=\"widget-terms widget-admin\">{string.Concat(Links.Select(l => $"<li><a href=\"{l.Href}\">{l.Label}</a></li>"))}</ul>");
}

/// <summary>The categories with the number of posts in each, each linking to its page.</summary>
public sealed class CategoryListWidget(IPublicContent content) : IWidget
{
    public static readonly WidgetDefinition Definition = new(
        "category-list", "Category list", "The categories of the blog, each linking to its posts.",
        [WidgetTerms.MaxSetting]);

    public async Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
        WidgetTerms.List(await content.GetCategoriesAsync(cancellationToken).ConfigureAwait(false), "category", WidgetTerms.MaxOf(context));
}

/// <summary>The newest published posts as a list of links (BlogEngine's Post List).</summary>
public sealed class PostListWidget(IPublicContent content) : IWidget
{
    public static readonly WidgetDefinition Definition = new(
        "post-list", "Post list", "The newest posts of the blog, each linking to its post.",
        [new PluginSettingDefinition("Max", "How many", "The most to show (1 to 50).", PluginSettingKind.Number, "5")]);

    public async Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var max = int.TryParse(context.Settings.GetValueOrDefault("Max"), out var parsed) ? Math.Clamp(parsed, 1, 50) : 5;
        var list = await content.ListPostsAsync(1, max, null, null, cancellationToken).ConfigureAwait(false);
        if (list.Posts.Count == 0)
        {
            return string.Empty;
        }

        var items = list.Posts.Take(max).Select(p => $"<li><a href=\"/post/{Uri.EscapeDataString(p.Slug)}\">{WebUtility.HtmlEncode(p.Title)}</a></li>");
        return $"<ul class=\"widget-terms widget-post-list\">{string.Concat(items)}</ul>";
    }
}

/// <summary>The pages of the site menu, with their sub-pages, as a list of links (BlogEngine's Page List).</summary>
public sealed class PageListWidget(IPublicContent content) : IWidget
{
    public static readonly WidgetDefinition Definition = new(
        "page-list", "Page list", "The pages of the site menu, each linking to its page.");

    public async Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var menu = await content.GetMenuAsync(cancellationToken).ConfigureAwait(false);
        return menu.Count == 0 ? string.Empty : $"<ul class=\"widget-terms widget-page-list\">{Items(menu)}</ul>";
    }

    private static string Items(IReadOnlyList<PublicMenuItem> items) =>
        string.Concat(items.Select(i => $"<li><a href=\"/page/{Uri.EscapeDataString(i.Slug)}\">{WebUtility.HtmlEncode(i.Title)}</a>{(i.Children.Count > 0 ? $"<ul>{Items(i.Children)}</ul>" : string.Empty)}</li>"));
}

internal static class WidgetTerms
{
    public static readonly PluginSettingDefinition MaxSetting = new("Max", "How many", "The most to show (1 to 100).", PluginSettingKind.Number, "30");

    public static int MaxOf(WidgetContext context) =>
        int.TryParse(context.Settings.GetValueOrDefault("Max"), out var max) ? Math.Clamp(max, 1, 100) : 30;

    /// <summary>The terms as a list of links: names are HTML-encoded for the text and URL-encoded for the address, so a name with markup in it injects nothing.</summary>
    public static string List(IReadOnlyList<PublicTerm> terms, string route, int max)
    {
        if (terms.Count == 0)
        {
            return string.Empty;
        }

        var items = terms.Take(max).Select(t => $"<li><a href=\"/{route}/{Uri.EscapeDataString(t.Name)}\">{WebUtility.HtmlEncode(t.Name)}</a> <span>({t.Posts})</span></li>");
        return $"<ul class=\"widget-terms\">{string.Concat(items)}</ul>";
    }
}
