using Adventures.Architecture.Tests.Support;
using Xunit;

namespace Adventures.Architecture.Tests;

/// <summary>
/// Architecture guard for the 2026-10 decoupling refactor (rules in ai-research-blog <c>docs/Claude-architecture-reference.md</c>).
/// Every rule is a ratchet: today's violations are listed with the stage that removes them. A NEW violation fails, and so
/// does a listed violation that no longer exists - the stage that fixes it must delete its line here (red, then green).
/// </summary>
public class DependencyRulesTests
{
    private static ProjectGraph Graph => ProjectGraph.Current;

    /// <summary>The project-reference graph as it stands. Strict: adding or removing an edge must update this list.</summary>
    private static readonly string[] RecordedEdges =
    [
        "Adventures.Common -> Adventures.Ioc",
        "Adventures.Data.NQuad -> Adventures.Entities",
        "Adventures.Entities -> Adventures.Common",
        "Adventures.Identity -> Adventures.Data",
        "Adventures.Identity -> Adventures.Security",
        "Adventures.WebApi -> Adventures.Data.NQuad",
        "Adventures.WebApi -> Adventures.Entities",
        "Adventures.WebApi -> Adventures.Security",
    ];

    [Fact]
    public void Project_reference_graph_matches_the_recorded_graph()
    {
        Assert.Equal(RecordedEdges, Graph.Edges);
    }

    [Fact]
    public void Project_references_have_no_cycles()
    {
        foreach (var start in Graph.Projects.Keys)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>(Graph.Projects[start].ProjectReferences);
            while (stack.Count > 0)
            {
                var next = stack.Pop();
                Assert.NotEqual(start, next);
                if (seen.Add(next) && Graph.Projects.TryGetValue(next, out var project))
                {
                    foreach (var reference in project.ProjectReferences)
                    {
                        stack.Push(reference);
                    }
                }
            }
        }
    }

    // Rule 1: Common references nothing of ours except its own Ioc; Entities references Common only.
    [Fact]
    public void Common_and_Entities_stay_at_the_bottom()
    {
        AssertRatchet(
            "Common/Entities may only reference Common/Ioc",
            Edges("Adventures.Common", "Adventures.Entities").Where(e => !AllowedBottom.Contains(e.To)),
            []);
    }

    private static readonly HashSet<string> AllowedBottom = ["Adventures.Common", "Adventures.Ioc"];

    // Rule 2: Security and Identity reference no storage project.
    [Fact]
    public void Security_and_Identity_reference_no_store()
    {
        AssertRatchet(
            "Security/Identity must not reference a store project",
            Edges("Adventures.Security", "Adventures.Identity").Where(e => IsStore(e.To)),
            ["Adventures.Identity -> Adventures.Data"]); // removed by S5: the Postgres credential store moves to Data.Postgres
    }

    // Rule 3: each store project references the Data abstractions, not Entities alone.
    [Fact]
    public void Store_projects_build_on_Adventures_Data()
    {
        var missing = new[] { "Adventures.Data.NQuad" }
            .Where(store => !Graph.Projects[store].ProjectReferences.Contains("Adventures.Data"))
            .Select(store => $"{store} -> Adventures.Data (missing)");
        AssertRatchet(
            "Store projects must reference Adventures.Data",
            missing.Select(m => (From: m, To: string.Empty)),
            ["Adventures.Data.NQuad -> Adventures.Data (missing)"]); // fixed by S5 (Data split)
    }

    // Rule 5: WebApi references no store project.
    [Fact]
    public void WebApi_references_no_store()
    {
        AssertRatchet(
            "WebApi must not reference a store project",
            Edges("Adventures.WebApi").Where(e => IsStore(e.To)),
            ["Adventures.WebApi -> Adventures.Data.NQuad"]); // removed by S5: probes by interface
    }

    // Rule 6: Security core carries no ASP.NET Core package.
    [Fact]
    public void Security_core_has_no_AspNetCore_package()
    {
        var offenders = Graph.Projects["Adventures.Security"].PackageReferences
            .Where(p => p.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Select(p => (From: "Adventures.Security", To: p));
        AssertRatchet(
            "Security must not reference ASP.NET Core packages",
            offenders,
            ["Adventures.Security -> Microsoft.AspNetCore.Authentication.JwtBearer"]); // removed by S6 (Security.AspNetCore)
    }

    // Rule R17: no source files at a project root. Recorded counts may only fall.
    [Fact]
    public void Source_files_at_project_roots_only_fall()
    {
        var recorded = new Dictionary<string, int>
        {
            ["Adventures.Common"] = 0,
            ["Adventures.Data"] = 9,
            ["Adventures.Data.NQuad"] = 14,
            ["Adventures.Entities"] = 67,
            ["Adventures.Identity"] = 10,
            ["Adventures.Ioc"] = 0,
            ["Adventures.Security"] = 17,
            ["Adventures.WebApi"] = 15,
        };

        var actual = Graph.Projects.ToDictionary(p => p.Key, p => p.Value.SourceFilesAtProjectRoot);
        Assert.Equal(recorded.OrderBy(r => r.Key), actual.OrderBy(a => a.Key));
    }

    private static bool IsStore(string project) =>
        project is "Adventures.Data" or "Adventures.Data.NQuad" or "Adventures.Data.Postgres" or "Adventures.Data.Sql";

    private static IEnumerable<(string From, string To)> Edges(params string[] from) =>
        from.Where(Graph.Projects.ContainsKey)
            .SelectMany(f => Graph.Projects[f].ProjectReferences.Select(r => (From: f, To: r)));

    private static void AssertRatchet(string rule, IEnumerable<(string From, string To)> violations, string[] known)
    {
        var actual = violations
            .Select(v => v.To.Length == 0 ? v.From : $"{v.From} -> {v.To}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            actual.SequenceEqual(known.OrderBy(k => k, StringComparer.Ordinal)),
            $"{rule}.{Environment.NewLine}Actual violations: [{string.Join("; ", actual)}]{Environment.NewLine}Recorded: [{string.Join("; ", known)}]");
    }
}
