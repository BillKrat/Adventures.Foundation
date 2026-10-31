// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Common.Exceptions;

namespace Adventures.Blog;

/// <summary>
/// The page rules: a page cannot be its own parent or sit under one of its own descendants (no cycles in the tree); only one
/// page is the front page (setting it on one clears it on the others); an empty slug is made from the title; a new page gets
/// a publication date; a slug is unique among pages (see <see cref="SlugRules"/>). Everything else is the generic Bll (required fields).
/// </summary>
public sealed class PageBll(IEntityDal<Page> pages) : EntityBll<Page>(pages), IPageBll
{
    public override async Task<Page> CreateAsync(Page entity, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(entity, selfId: null, storedSlug: null, cancellationToken).ConfigureAwait(false);
        var created = await base.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        await ClearOtherFrontPagesAsync(created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    public override async Task UpdateAsync(Page entity, CancellationToken cancellationToken = default)
    {
        var id = entity.EntityId?.Value;
        var stored = id is null ? null : await GetAsync(id, cancellationToken).ConfigureAwait(false);
        await PrepareAsync(entity, id, stored?.GetValue("Slug") as string, cancellationToken).ConfigureAwait(false);
        await RejectCycleAsync(entity, cancellationToken).ConfigureAwait(false);
        await base.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        await ClearOtherFrontPagesAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    private async Task PrepareAsync(Page page, string? selfId, string? storedSlug, CancellationToken cancellationToken)
    {
        var title = page.GetValue("Title") as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(page.GetValue("Slug") as string) || !string.IsNullOrWhiteSpace(title))
        {
            var others = (await ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(p => p.EntityId is not null)
                .Select(p => (p.EntityId!.Value, p.GetValue("Slug") as string ?? string.Empty, p.GetValue("Title") as string ?? string.Empty));
            var resolved = SlugRules.Resolve(page.GetValue("Slug") as string, title, selfId, storedSlug, others);
            if (resolved.Length > 0)
            {
                page.Set("Slug", Guid.NewGuid().ToString(), resolved);
            }
        }

        if (page.GetValue("DatePublished") is null)
        {
            page.Set("DatePublished", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>"Direct Corpus Interaction" becomes "direct-corpus-interaction": lower case, letters and digits, single hyphens.</summary>
    public static string Slugify(string title)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var c in title.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private async Task RejectCycleAsync(Page page, CancellationToken cancellationToken)
    {
        var id = page.EntityId?.Value;
        if (id is null || page.GetValue("Parent") is not string parentId || parentId.Length == 0)
        {
            return;
        }

        if (string.Equals(parentId, id, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("A page cannot be its own parent.");
        }

        var parents = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => p.EntityId is not null)
            .ToDictionary(p => p.EntityId!.Value, p => p.GetValue("Parent") as string, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = parentId; !string.IsNullOrEmpty(current) && seen.Add(current); current = parents.GetValueOrDefault(current))
        {
            if (string.Equals(current, id, StringComparison.Ordinal))
            {
                throw new BusinessRuleException("A page cannot be moved under one of its own sub-pages.");
            }
        }
    }

    private async Task ClearOtherFrontPagesAsync(Page page, CancellationToken cancellationToken)
    {
        if (page.GetValue("IsFrontPage") is not true)
        {
            return;
        }

        foreach (var other in (await ListAsync(cancellationToken).ConfigureAwait(false))
                     .Where(p => p.EntityId?.Value != page.EntityId?.Value && p.GetValue("IsFrontPage") is true))
        {
            other.Set("IsFrontPage", Guid.NewGuid().ToString(), false);
            await Dal.UpdateAsync(other, cancellationToken).ConfigureAwait(false);
        }
    }
}
