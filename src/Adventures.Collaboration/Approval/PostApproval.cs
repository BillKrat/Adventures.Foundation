using Adventures.Entities;
using Adventures.MEF;
namespace Adventures.Collaboration;

/// <summary>The review states of a collaboration post (a post under review, and a collaboration item). Empty means the post is not under review.</summary>
public static class PostApproval
{
    public const string NeedsReview = "Needs review";
    public const string Approved = "Approved";
    public const string ChangesRequested = "Changes requested";

    public static readonly IReadOnlyList<string> All = [NeedsReview, Approved, ChangesRequested];

    /// <summary>The category whose posts are reviewed (the collaboration channel); posts written by the authoring surface into it start as <see cref="NeedsReview"/>.</summary>
    public const string CollaborationCategory = "Collaboration";
}
