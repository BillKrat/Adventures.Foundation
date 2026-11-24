using Adventures.Entities;

namespace Adventures.Blog;

/// <summary>The seeds of the blog content model, comments, site settings, widget placements and language files.</summary>
public static class BlogSeeds
{
    /// <summary>In application order.</summary>
    public static IReadOnlyList<SeedEntry> All { get; } =
    [
        new("seed-006-content.nq", BlogConstants.Content.SeedName),
        new("seed-007-post-taxonomy.nq", BlogConstants.Content.TaxonomySeedName),
        new("seed-008-post-revision.nq", BlogConstants.Content.RevisionSeedName),
        new("seed-013-post-approval.nq", BlogConstants.Content.ApprovalSeedName),
        new("seed-022-content-format.nq", BlogConstants.Content.ContentFormatSeedName),
        new("seed-021-visitor-comments.nq", BlogConstants.Content.VisitorCommentSeedName),
        new("seed-025-widget-placements.nq", WidgetConstants.SeedName),
        new("seed-026-language-files.nq", LanguageConstants.SeedName),
        new("seed-011-site-settings.nq", BlogConstants.Site.SeedName),
    ];
}

