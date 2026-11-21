using System.Text.RegularExpressions;

namespace Adventures.Blog;

/// <summary>
/// What an owner may add to the sanitiser's allow-list on its Customize screen: extra tags, extra attributes and extra URL schemes. There is a line the
/// list cannot cross: scripts, loaders and frames, event handlers and script-capable addresses stay refused, because allowing them is the same as
/// switching the plug-in off, and that is its own switch with its own warning. The line is enforced when a value is saved
/// (<see cref="ValidateTags"/> and friends) and again when the list is built (<see cref="From"/>), so a value written another way is still ignored.
/// </summary>
public sealed partial record HtmlAllowList(IReadOnlyList<string> Tags, IReadOnlyList<string> Attributes, IReadOnlyList<string> Schemes)
{
    private static readonly string[] DefaultSchemes = ["http", "https", "mailto"];

    /// <summary>Today's fixed rules: nothing extra, and the three schemes the visual editor uses.</summary>
    public static readonly HtmlAllowList Default = new([], [], DefaultSchemes);

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
        return new HtmlAllowList(tags, attributes, schemes.Count == 0 ? DefaultSchemes : schemes);
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

    private static bool IsNeverAttribute(string name) => name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || NeverAttributes.Contains(name);

    private static IEnumerable<string> Names(IReadOnlyDictionary<string, string> settings, string name) =>
        settings.TryGetValue(name, out var value) ? Names(value) : [];

    private static IEnumerable<string> Names(string value) =>
        value.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => n.ToLowerInvariant()).Distinct();
}
