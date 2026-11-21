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

/// <summary>The tags with the number of posts using each, most used first, each linking to its page.</summary>
public sealed class TagCloudWidget(IPublicContent content) : IWidget
{
    public static readonly WidgetDefinition Definition = new(
        "tag-cloud", "Tag cloud", "The tags of the blog, most used first, each linking to its posts.",
        [WidgetTerms.MaxSetting]);

    public async Task<string> RenderAsync(WidgetContext context, CancellationToken cancellationToken) =>
        WidgetTerms.List(await content.GetTagsAsync(cancellationToken).ConfigureAwait(false), "tag", WidgetTerms.MaxOf(context));
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
