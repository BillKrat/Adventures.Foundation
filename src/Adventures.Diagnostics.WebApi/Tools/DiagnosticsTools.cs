using Adventures.Tools;

namespace Adventures.Diagnostics.WebApi;

/// <summary>The machine diagnostics surface (<see cref="McpDiagnosticsController"/>) described as tools: read-only, scope <c>mcp.diagnostics.read</c>.</summary>
public static class DiagnosticsTools
{
    public const string Scope = McpDiagnosticsController.RequiredScope;

    public static IReadOnlyList<IAppTool> All { get; } =
    [
        new AppTool(
            "diagnose_request",
            "Everything recorded for one request: its exceptions, the kinds of failure they belong to, and its log lines, found by the CorrelationId (the id shown in an error message or the X-Correlation-Id response header).",
            """{ "type": "object", "properties": { "correlationId": { "type": "string", "description": "The request's correlation id." } }, "required": ["correlationId"] }""",
            Scope, "GET", "api/diagnostics/{correlationId}"),
        new AppTool(
            "recent_exceptions",
            "The most recent active exceptions, newest first.",
            """{ "type": "object", "properties": { "max": { "type": "integer", "description": "How many (1-500, default 50)." } } }""",
            Scope, "GET", "api/diagnostics/recent"),
        new AppTool(
            "exception_signatures",
            "Every known kind of failure with how often and when it was seen and its triage status.",
            """{ "type": "object", "properties": {} }""",
            Scope, "GET", "api/diagnostics/signatures"),
    ];
}
