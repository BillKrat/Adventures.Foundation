using Adventures.MEF;

namespace Adventures.Collaboration;

/// <summary>
/// The built-in plug-ins. A plug-in is a bundle of contributions (an admin menu entry, a schema, endpoints, MCP tools); switching one off
/// hides its menu entry and makes its endpoints refuse, and keeps its data. Plug-ins loaded from outside the app (MEF) are a later step; the
/// stored state and the Plugins page do not change when they arrive.
/// </summary>
public static class CollaborationPlugins
{
    public const string DeveloperCollaboration = "developer-collaboration";
    public const string ResearchCollaboration = "research-collaboration";
    public const string Decisions = "decisions";

    public static readonly IReadOnlyList<PluginDefinition> Definitions =
    [
        new(DeveloperCollaboration, "Developer Collaboration", "Review requests between the people and AIs building the project: branch, repositories, test steps, Approve and Request changes.", "1.0", 0),
        new(ResearchCollaboration, "Research Collaboration", "Research questions and findings: a conversation with sources, accepted into a post when settled. No approval.", "1.0", 1),
        new(Decisions, "Decisions", "The page listing every decision recorded in a conversation, across posts and collaboration items.", "1.0", 2),
    ];

    /// <summary>The plug-in that allows items of this collaboration kind.</summary>
    public static string? ForKind(string kind) => kind switch
    {
        CollaborationKinds.Developer => DeveloperCollaboration,
        CollaborationKinds.Research => ResearchCollaboration,
        _ => null,
    };
}
