using Ganss.Xss;

namespace Adventures.Blog;

/// <summary>
/// The one place HTML from the visual editor is made safe, on the way in (when a post or page is saved) and again on the way out (when the
/// public site serves it). It keeps what a word processor produces (text formatting, lists, tables, links, pictures, alignment and colour) and
/// removes everything that can run script, submit a form, load a page from elsewhere or reach the visitor session: scripts, event handlers,
/// frames, forms, style sheets, <c>javascript:</c> and <c>data:</c> addresses, and any CSS that loads a resource. Every new window link
/// gets <c>rel="noopener noreferrer"</c>.
/// </summary>
public static class HtmlContent
{
    /// <summary>Returns the safe form of the HTML; empty for nothing.</summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // a new instance per call: the sanitiser is configurable and not meant to be shared while it is in use
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { LocalName: "a" } link && link.HasAttribute("target"))
            {
                link.SetAttribute("rel", "noopener noreferrer");
            }
        };
        sanitizer.AllowedAttributes.Add("target");

        // interactive and form elements have no place in a post; and a CSS property that can fetch a resource (a background image, a list marker image) is dropped
        foreach (var tag in new[] { "form", "input", "button", "select", "option", "optgroup", "textarea", "fieldset", "legend", "label", "output", "datalist" })
        {
            sanitizer.AllowedTags.Remove(tag);
        }

        foreach (var property in new[] { "background", "background-image", "list-style-image", "border-image", "cursor", "content", "behavior", "-moz-binding" })
        {
            sanitizer.AllowedCssProperties.Remove(property);
        }

        return sanitizer.Sanitize(html);
    }
}
