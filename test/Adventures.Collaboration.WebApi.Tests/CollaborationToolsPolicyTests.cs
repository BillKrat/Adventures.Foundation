using Adventures.Collaboration.WebApi;
using Adventures.Tools;
using Xunit;

namespace Adventures.Collaboration.WebApi.Tests;

/// <summary>The collaboration tools after the 2026-11-24 decision: the authoring account can list, read, revise and comment on any item, and still can approve or decide none.</summary>
public sealed class CollaborationToolsPolicyTests
{
    [Fact]
    public void There_is_a_tool_that_lists_every_item_and_it_is_read_only()
    {
        var tool = Assert.Single(CollaborationAuthoringTools.All.Cast<AppTool>(), t => t.Name == "list_collaboration");
        Assert.Equal("GET", tool.Method);
        Assert.Equal("api/author/collaboration/all", tool.PathTemplate);
        Assert.Equal(CollaborationAuthoringTools.Scope, tool.RequiredScope);
    }

    [Fact]
    public void No_tool_can_approve_decide_or_accept()
    {
        Assert.DoesNotContain(CollaborationAuthoringTools.All.Cast<AppTool>(), t => t.PathTemplate.Contains("approve", StringComparison.OrdinalIgnoreCase)
            || t.PathTemplate.Contains("accept", StringComparison.OrdinalIgnoreCase) || t.PathTemplate.Contains("request-changes", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("decision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_authoring_routes_are_the_documented_ones()
    {
        Assert.Equal(
            ["add_collaboration_comment", "create_collaboration_item", "get_collaboration_item", "list_collaboration", "list_collaboration_comments", "list_my_collaboration", "update_collaboration_item"],
            CollaborationAuthoringTools.All.Select(t => t.Name).Order(StringComparer.Ordinal));
    }
}
