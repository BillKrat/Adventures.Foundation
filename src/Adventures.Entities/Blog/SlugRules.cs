// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// One address (slug) belongs to one post, and to one page: the reader finds content by slug, so two entries sharing one would show
/// the wrong text. A slug made from the title is made unique by a numeric suffix (<c>my-post</c>, <c>my-post-2</c>); a slug an author
/// typed is refused when another entry already has it, naming that entry. An update that leaves an existing slug unchanged is allowed
/// (an old duplicate must not make the entry impossible to save). Comparison ignores case, as the reader does.
/// </summary>
public static class SlugRules
{
    /// <param name="slug">The slug on the entry being saved (may be empty).</param>
    /// <param name="title">The entry's title, the source of a generated slug.</param>
    /// <param name="selfId">The entry's own id when it is an update, so it does not clash with itself.</param>
    /// <param name="storedSlug">The slug the entry has stored now, for an update.</param>
    /// <param name="others">Every other entry that can clash: (id, slug, title). Proposed revisions are not passed in.</param>
    public static string Resolve(string? slug, string title, string? selfId, string? storedSlug, IEnumerable<(string Id, string Slug, string Title)> others)
    {
        var taken = others
            .Where(o => !string.Equals(o.Id, selfId, StringComparison.Ordinal) && !string.IsNullOrEmpty(o.Slug))
            .GroupBy(o => o.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Title, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(slug))
        {
            var wanted = slug.Trim();
            if (!taken.TryGetValue(wanted, out var holder) || string.Equals(storedSlug, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return wanted;
            }

            throw new BusinessRuleException($"The address \"{wanted}\" is already used by \"{holder}\". Choose another address.");
        }

        var baseSlug = PageBll.Slugify(title);
        if (baseSlug.Length == 0 || !taken.ContainsKey(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseSlug}-{n}";
            if (!taken.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }
}
