using Adventures.Entities;

namespace Adventures.Collaboration;

/// <summary>The seeds of the conversation under a post, collaboration items and the Research kind.</summary>
public static class CollaborationSeeds
{
    /// <summary>In application order.</summary>
    public static IReadOnlyList<SeedEntry> All { get; } =
    [
        new("seed-014-comments.nq", CollaborationConstants.CommentsSeedName),
        new("seed-015-collaboration.nq", CollaborationConstants.SeedName),
        new("seed-017-research.nq", CollaborationConstants.ResearchSeedName),
    ];
}

