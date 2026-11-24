namespace Adventures.Security;

/// <summary>The machine scopes the API and the MCP gateway agree on, named once (R4 of the host reuse review; each used to be a string repeated in several places).</summary>
public static class Scopes
{
    /// <summary>Draft authoring and collaboration tools: write drafts, never publish.</summary>
    public const string ContentDraft = "content.draft";

    /// <summary>The read-only diagnostics tools.</summary>
    public const string DiagnosticsRead = "mcp.diagnostics.read";

    /// <summary>The cross-site proof endpoint.</summary>
    public const string Hello = "mcp.hello";
}
