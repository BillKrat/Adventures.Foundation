using Adventures.Common.Exceptions;
using Adventures.Entities;

namespace Adventures.Blog;

/// <summary>
/// How the Content of a post or page is written. A person writing in the admin gets <see cref="Html"/> (the Word-like visual editor);
/// an AI author and every post that existed before the field did are <see cref="Markdown"/>. The format is chosen when the item is created and cannot
/// change afterwards, so what is stored always matches how it is shown. HTML is made safe when it is saved (see <see cref="HtmlContent"/>).
/// </summary>
public static class ContentFormats
{
    public const string FieldName = "ContentFormat";
    public const string Markdown = "Markdown";
    public const string Html = "Html";

    public static readonly IReadOnlyList<string> All = [Markdown, Html];

    /// <summary>The format of a stored item: Markdown when it has none (it was written before the field existed).</summary>
    public static string Of(DynamicEntity entity) =>
        entity.GetValue(FieldName) is string { Length: > 0 } format ? format : Markdown;

    /// <summary>
    /// Applies the rules when an item is saved: a missing format becomes Markdown (or the stored one on an update), an unknown one is refused, a stored
    /// format cannot change, and HTML content is sanitised (by the given function, which the sanitiser plug-in decides; the fixed rules when none is given). Does nothing for a store whose schema has no such field.
    /// </summary>
    public static void Prepare(DynamicEntity entity, string? storedFormat, Func<string?, string>? sanitize = null)
    {
        if (!entity.Fields.Any(field => string.Equals(field.Name, FieldName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var format = entity.GetValue(FieldName) as string;
        if (string.IsNullOrWhiteSpace(format))
        {
            format = string.IsNullOrEmpty(storedFormat) ? Markdown : storedFormat;
            entity.Set(FieldName, Guid.NewGuid().ToString(), format);
        }

        if (!All.Contains(format, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"The format must be one of: {string.Join(", ", All)}.");
        }

        if (!string.IsNullOrEmpty(storedFormat) && !string.Equals(storedFormat, format, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("The format of an existing post or page cannot be changed.");
        }

        if (format == Html && entity.GetValue("Content") is string content)
        {
            entity.Set("Content", Guid.NewGuid().ToString(), (sanitize ?? HtmlContent.Sanitize)(content));
        }
    }

    /// <summary>The sanitising function for this request from the seam, or null (the fixed rules) when a host registered none.</summary>
    internal static async Task<Func<string?, string>?> SanitizerAsync(IHtmlContentSanitizer? sanitizer, CancellationToken cancellationToken) =>
        sanitizer is null ? null : await sanitizer.GetAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>For the admin form: a new item with no format is an Html one (people write in the visual editor).</summary>
    public static EntityDataModel DefaultForNewForm(EntityDataModel data, EntitySchema schema)
    {
        if (!schema.Fields.ContainsKey(FieldName) || (data.Values.TryGetValue(FieldName, out var chosen) && !string.IsNullOrWhiteSpace(chosen)))
        {
            return data;
        }

        var values = new Dictionary<string, string?>(data.Values, StringComparer.OrdinalIgnoreCase) { [FieldName] = Html };
        return data with { Values = values };
    }
}
