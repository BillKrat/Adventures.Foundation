using Adventures.Entities;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>Beta readiness: the dashboard shows each figure and list only to a person who may see what it counts. Admin access alone (a beta tester) sees none of them.</summary>
public sealed class DashboardRightsTests
{
    private static DashboardModel Full() => new(
        PublishedPosts: 5, DraftPosts: 2, PublishedPages: 3, DraftPages: 1, Exceptions: 9, CollaborationWaiting: 4, CommentsPending: 6,
        DraftPostList: [new DashboardItem("p1", "Secret draft post")],
        DraftPageList: [new DashboardItem("g1", "Secret draft page")],
        LatestComments: [new DashboardComment("c1", "Reader", "A post", "Pending", "2026-11-24", "an excerpt")]);

    private static HashSet<string> Rights_(params string[] rights) => [.. rights];

    [Fact]
    public void Admin_access_alone_sees_no_counts_and_no_lists_except_what_it_may_know()
    {
        var shown = DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess));

        Assert.Equal(0, shown.PublishedPosts + shown.DraftPosts + shown.PublishedPages + shown.DraftPages + shown.Exceptions + shown.CommentsPending + shown.CollaborationWaiting);
        Assert.Empty(shown.DraftPostList);
        Assert.Empty(shown.DraftPageList);
        Assert.Empty(shown.LatestComments);
    }

    [Fact]
    public void Each_figure_needs_the_right_to_see_what_it_counts()
    {
        Assert.Equal(2, DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.PostsRead)).DraftPosts);
        Assert.Single(DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.PostsRead)).DraftPostList);
        Assert.Empty(DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.PostsRead)).DraftPageList);
        Assert.Single(DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.PagesRead)).DraftPageList);
        Assert.Equal(9, DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.DiagnosticsRead)).Exceptions);
        Assert.Equal(4, DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.CollaborationRead)).CollaborationWaiting);
        var moderator = DashboardSummary.Restrict(Full(), Rights_(Rights.AdminAccess, Rights.CommentsModerate));
        Assert.Equal(6, moderator.CommentsPending);
        Assert.Single(moderator.LatestComments);
    }

    [Fact]
    public void An_administrator_sees_everything()
    {
        var shown = DashboardSummary.Restrict(Full(), Rights_(Rights.All));

        Assert.Equal(Full(), shown with { }, new DashboardModelComparer());
    }

    private sealed class DashboardModelComparer : IEqualityComparer<DashboardModel>
    {
        public bool Equals(DashboardModel? a, DashboardModel? b) =>
            a is not null && b is not null && a.PublishedPosts == b.PublishedPosts && a.DraftPosts == b.DraftPosts && a.PublishedPages == b.PublishedPages && a.DraftPages == b.DraftPages
            && a.Exceptions == b.Exceptions && a.CollaborationWaiting == b.CollaborationWaiting && a.CommentsPending == b.CommentsPending
            && a.DraftPostList.Count == b.DraftPostList.Count && a.DraftPageList.Count == b.DraftPageList.Count && a.LatestComments.Count == b.LatestComments.Count;

        public int GetHashCode(DashboardModel obj) => obj.PublishedPosts;
    }
}
