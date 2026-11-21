using System.Text.RegularExpressions;

namespace Adventures.Blog;

/// <summary>
/// What an owner may add to the sanitiser's allow-list on its Customize screen: extra tags, extra attributes and extra URL schemes. There is a line the
/// list cannot cross: scripts, loaders and frames, event handlers and script-capable addresses stay refused, because allowing them is the same as
/// switching the plug-in off, and that is its own switch with its own warning. The line is enforced when a value is saved
/// (<see cref="ValidateTags"/> and friends) and again when the list is built (<see cref="From"/>), so a value written another way is still ignored.
/// </summary>
public sealed partial record HtmlAllowList(IReadOnlyList<string> Tags, IReadOnlyList<string> Attributes, IReadOnlyList<string> Schemes, IReadOnlyList<string> Classes)
{
    private static readonly string[] DefaultSchemes = ["http", "https", "mailto"];

    /// <summary>Today's fixed rules: nothing extra, and the three schemes the visual editor uses.</summary>
    public static readonly HtmlAllowList Default = new([], [], DefaultSchemes, []);

    /// <summary>The CSS classes the blog itself uses in the HTML its widgets return (the tag cloud and the term lists); they are always kept, so the public page can style them.</summary>
    public static readonly IReadOnlyList<string> BuiltInClasses =
    [
        "tag-cloud", "tag-cloud__item", "tag-cloud__tag", "tag-cloud__count", "tag-cloud__size-1", "tag-cloud__size-2", "tag-cloud__size-3", "tag-cloud__size-4", "tag-cloud__size-5", "widget-terms",
    ];

    /// <summary>Class names that belong to the app's own interface (overlays, dialogs, the busy gears, admin and widget screens); post and widget HTML may not use them, so it cannot dress up as the app.</summary>
    private static readonly string[] AppClassPrefixes = ["cdk-", "mat-", "mdc-", "page__", "page--", "busy", "adv-", "widget__", "widget--", "admin-", "plugin-", "zone", "placement"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial Regex ClassName();

    private static readonly HashSet<string> NeverTags = new(
        ["script", "style", "object", "embed", "applet", "base", "meta", "link", "iframe", "frame", "frameset", "noscript", "template", "svg", "math",
         "form", "input", "button", "select", "option", "optgroup", "textarea", "fieldset", "legend", "label", "output", "datalist"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> NeverAttributes = new(["srcdoc", "formaction", "action", "xlink:href", "ping"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SafeSchemes = new(["http", "https", "mailto", "tel", "ftp", "sms"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex TagName();

    [GeneratedRegex("^[a-z][a-z0-9_:.-]*$")]
    private static partial Regex AttributeName();

    [GeneratedRegex("^[a-z][a-z0-9+.-]*$")]
    private static partial Regex SchemeName();

    /// <summary>The list for the stored settings; anything past the line is dropped, and an empty scheme list means the default schemes.</summary>
    public static HtmlAllowList From(IReadOnlyDictionary<string, string> settings)
    {
        var tags = Names(settings, HtmlSanitizerPlugin.ExtraAllowedTags).Where(t => TagName().IsMatch(t) && !NeverTags.Contains(t)).ToList();
        var attributes = Names(settings, HtmlSanitizerPlugin.ExtraAllowedAttributes).Where(a => AttributeName().IsMatch(a) && !IsNeverAttribute(a)).ToList();
        var schemes = Names(settings, HtmlSanitizerPlugin.AllowedUrlSchemes).Where(s => SchemeName().IsMatch(s) && SafeSchemes.Contains(s)).ToList();
        var classes = Names(settings, HtmlSanitizerPlugin.ExtraAllowedClasses, lower: false).Where(c => ClassName().IsMatch(c) && !IsAppClass(c)).ToList();
        return new HtmlAllowList(tags, attributes, schemes.Count == 0 ? DefaultSchemes : schemes, classes);
    }

    /// <summary>Null when the extra tags are fine, otherwise what is wrong.</summary>
    public static string? ValidateTags(string value)
    {
        foreach (var tag in Names(value))
        {
            if (!TagName().IsMatch(tag))
            {
                return $"'{tag}' is not a tag name (letters, digits and hyphens, starting with a letter).";
            }

            if (NeverTags.Contains(tag))
            {
                return $"'{tag}' cannot be allowed here: it can run script, load another page or submit data. To allow it, switch HTML sanitising off.";
            }
        }

        return null;
    }

    /// <summary>Null when the extra attributes are fine, otherwise what is wrong.</summary>
    public static string? ValidateAttributes(string value)
    {
        foreach (var attribute in Names(value))
        {
            if (!AttributeName().IsMatch(attribute))
            {
                return $"'{attribute}' is not an attribute name.";
            }

            if (IsNeverAttribute(attribute))
            {
                return $"'{attribute}' cannot be allowed here: it can run script or load another page. To allow it, switch HTML sanitising off.";
            }
        }

        return null;
    }

    /// <summary>Null when the schemes are fine, otherwise what is wrong. At least one scheme is needed or no link would work.</summary>
    public static string? ValidateSchemes(string value)
    {
        var schemes = Names(value).ToList();
        if (schemes.Count == 0)
        {
            return "Allow at least one URL scheme (for example http, https and mailto).";
        }

        foreach (var scheme in schemes)
        {
            if (!SafeSchemes.Contains(scheme))
            {
                return $"'{scheme}' cannot be allowed. The schemes that can be allowed are: {string.Join(", ", SafeSchemes.Order())}.";
            }
        }

        return null;
    }

    /// <summary>Null when the extra CSS classes are fine, otherwise what is wrong. Names that belong to the app's own interface are refused.</summary>
    public static string? ValidateClasses(string value)
    {
        foreach (var name in Names(value, lower: false))
        {
            if (!ClassName().IsMatch(name))
            {
                return $"'{name}' is not a CSS class name (letters, digits, hyphens and underscores, starting with a letter or underscore).";
            }

            if (IsAppClass(name))
            {
                return $"'{name}' cannot be allowed: it is used by the app's own interface, and content that used it could pass for the app.";
            }
        }

        return null;
    }

    private static bool IsAppClass(string name) => AppClassPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsNeverAttribute(string name) => name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || NeverAttributes.Contains(name);

    private static IEnumerable<string> Names(IReadOnlyDictionary<string, string> settings, string name, bool lower = true) =>
        settings.TryGetValue(name, out var value) ? Names(value, lower) : [];

    private static IEnumerable<string> Names(string value, bool lower = true) =>
        value.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => lower ? n.ToLowerInvariant() : n).Distinct();
}
